using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Voitures (corps portant CarDynamics : CORRIS, GIFU, KEKMET, FLATBED... a la racine de la scene, et les
    // voitures de joueur rangees sous une autre racine : le taxi JOBS/TAXIJOB/MACHTWAGEN -- IsCarBody, CarRoot).
    // Celui qui conduit fait autorite : il envoie position et vitesses 20 fois/s ; chez les autres
    // la voiture devient cinematique et suit. Sans conducteur, l'hote recale toutes les 2 s les
    // voitures qui ont derive (arrivee d'un invite, voiture poussee...).
    // Numeros : chaque voiture a une cle (son nom, + "#rang" entre homonymes) ; l'hote numerote les siennes dans
    // l'ordre ou il les trouve (table, envoyee a chaque invite qui donne sa liste, et a tous quand elle grandit) ;
    // chaque invite rattache ses voitures a ces numeros par la cle. La liste est revue toutes les 20 s (voitures
    // activees plus tard : taxi, voiture pretee, HYDROCOPTER, JOKKIS ; cyclomoteur recree : meme cle, nouveau corps).
    // Une voiture absente de la table de l'hote n'est pas suivie (journal).
    // Conduite : automate 'PlayerTrigger' d'un objet DriveTrigger* de la voiture, etat 'Player in car'.
    // Le conducteur envoie aussi regime, accelerateur et braquage : chez les autres, la copie
    // cinematique a son Drivetrain, ses Wheel et son AxisCarController coupes ; le regime et
    // l'accelerateur recus nourrissent le son (et le Drivetrain de la copie : sa pompe de fosse septique
    // lit ce regime, EngineRpm), et les roues sont tournees et braquees ici
    // (Wheel.CalcWheelMovement du jeu, sans sa physique). Le bruit du moteur est fait par des objets
    // a AudioSource sous un conteneur 'Sounds' (KEKMET/LOD/Sounds/SoundKekmet...), allumes par le
    // contact quand le moteur tourne : le conducteur envoie lesquels sont actifs, avec leur hauteur
    // et leur volume (leur automate les calcule d'apres le vehicule conduit LOCALEMENT : coupe ici).
    // Moteur en marche ou non : le conducteur l'envoie (bit 15 du masque des sons : son automate Starter en "Running").
    // Sur la copie, l'automate Starter rejoue la cle et refait sa propre simulation (melange, batterie, regime) : il
    // pouvait finir "Stall engine" alors que le moteur tournait chez le conducteur -- CORRIS « moteur coupe » chez
    // l'autre, sans son (ses bruits de moteur sont sous Simulation/ExhaustCorris, allume par "Start engine", pas sous
    // un conteneur 'Sounds') ; retour d'un joueur, 08/10. Un ecart qui dure 1,5 s est corrige : "Start engine" (puis
    // "Crank up" -> "Running" au regime recu), ou "Stall engine" si le moteur est coupe chez le conducteur.
    // Klaxon : objet CarHorn de la voiture (source en boucle, active tant qu'on klaxonne : touche du jeu, ou bouton
    // ButtonHorn de la GIFU / du BACHGLOTZ). Le conducteur envoie s'il est actif (bit 14 du masque des sons) ; la copie
    // l'allume ou l'eteint, apres la logique du jeu (LateUpdate). Avant : jamais entendu chez les autres (retour d'un
    // joueur, 08/10).
    // Moteur laisse tournant : celui qui sort de la voiture en garde la main (etat 2) tant que son moteur
    // tourne et que personne d'autre ne la prend : les autres entendent toujours ce moteur, sans
    // conducteur assis. A la fin de la copie, moteur, commandes et roues reprennent l'etat d'AVANT
    // (le jeu coupe le Drivetrain d'un moteur arrete : le rallumer de force le faisait caler et
    // redemarrer en boucle, sons superposes).
    // Etat de la simulation (batterie, temperature du moteur, amorcage/noyage du carburateur, bougies de
    // prechauffage, pression des freins a air, tirette de starter : SimVars) : seul celui qui fait autorite
    // la calcule vraiment. Il l'envoie une fois par seconde (les copies la reprennent : une batterie videe par
    // des feux rejoues sur une copie a l'arret ne compte pas), puis une derniere fois, fiable, en rendant la
    // voiture : le conducteur suivant part de cet etat. Voiture garee : l'hote envoie le sien toutes les 10 s.
    // La tirette de starter (commande, rejouee par Jobs) ne part qu'avec l'etat rendu.
    // Chaleur de l'habitacle : la source de chaleur de la voiture (HeatSource*, ecrite par son automate
    // CarTemp... d'apres le moteur, le chauffage, les portieres) est envoyee avec la voiture ; sur la
    // copie (moteur froid), elle est reprise apres la logique du jeu -- le passager se rechauffe aussi.
    // Objets poses dedans (coffre, banquette) : voir Props (CarUnder, Authority) ; un recalage d'un coup
    // de la voiture les emmene avec elle.
    public static class VehicleSync
    {
        class Sim { public string Key; public uint Hash; public FsmFloat Var; public bool Control; }

        class Car
        {
            public int Index;                   // rang dans la liste locale (les voitures trouvees plus tard s'ajoutent a la fin)
            public int Net = -1;                // numero sur le reseau : rang dans la table de l'hote (-1 : absente de sa table)
            public string Key;                  // nom, + "#rang" entre homonymes : la meme chez tous
            public string Name;
            public Transform T;
            public Rigidbody Body;
            public PlayMakerFSM Drive;
            public int RemoteDriver = -1;       // joueur qui la conduit chez lui (-1 : personne)
            public Vector3 RemoteHead;          // sa camera, repere de la voiture (envoyee avec la voiture : meme instant)
            public int RemoteBy = -1;           // joueur qui la fait avancer et tourner chez lui (conducteur ou moteur laisse tournant)
            public bool DtWas, AxisWas; public bool[] WheelsWas;
            public Dictionary<Joint, Vector2> JointsWas;   // attaches rendues incassables sur la copie
            public float NextJoints;
            public float LastRemote;
            public Vector3 Pos, Vel, AngVel;
            public Quaternion Rot;
            public bool Kinematic;
            public bool WasKinematic;
            public Dictionary<Rigidbody, RigidbodyInterpolation> InterpWas;   // copie : interpolation de la voiture et de ses pieces
            public bool Framewise;              // copie ou le joueur local est passager : deplacee a chaque image (Ride)
            public float SettleUntil;           // reprise d'une copie (voiture redevenue physique ici) : bridee jusque-la
            public Vector3 SettleVel, SettleAng;
            public Dictionary<Joint, Vector2> JointsLater;   // attaches rendues cassables a SettleUntil seulement
            public float NextLog;
            public Drivetrain Dt;
            public Wheel[] Wheels;
            public AxisCarController Axis;
            public bool Mod;                  // vehicule de mod sans CarDynamics (IsModVehicle)
            // Moteur d'un vehicule de mod (Poro : SnowmobileDrivetrain, proprietes CurrRpm et IsRunning ; son bruit les lit) et
            // son controleur (SnowmobileController : recalcule le regime a chaque pas physique) coupe sur la copie.
            public Component ModDt; public System.Reflection.PropertyInfo ModRpm, ModRun; public Behaviour ModCtrl; public bool ModCtrlWas;
            public SoundController Sound;     // coupe par le jeu quand le joueur local n'est pas au volant
            public bool SoundWasOn;
            public GameObject[] SoundObjs;
            public bool[] SoundObjsWas;
            public int SoundMask;
            public float[] SoundPitch, SoundVol;
            public float Rpm, Throttle, Steer;
            public FsmFloat Heat; public float RemoteHeat = float.NaN;   // temperature de la source de chaleur
            public PlayMakerFSM Starter;        // automate 'Starter' (etat "Running" : moteur en marche)
            public bool RemoteRunning;          // moteur en marche chez celui qui la fait rouler
            public float StarterOff;            // copie : debut de l'ecart entre son Starter et celui du conducteur (0 : aucun)
            public PlayMakerFSM KeyFsm; public bool KeyLooked; public float KeyCrankSince, LatchSince;   // cle de contact (StarterLatch)
            public GameObject Horn;             // klaxon (CarHorn) ; HornRemote : allume chez celui qui la fait rouler
            public bool HornRemote, HornWas, HornSet;
            public float[] WheelRot;
            public Sim[] Sims = new Sim[0];     // etat de la simulation (SimVars)
            public float AuthorityUntil;        // on vient de la rendre : l'instantane de l'hote (voiture garee) attend
            public bool SimLogged;
        }

        // Message Msg.Vehicle : joueur, numero de voiture, mode. Modes 0 garee, 1 conduite, 2 moteur tournant (pose,
        // vitesses...) ; 8 etat de la simulation ; 9 table de l'hote et 10 liste d'un invite (numero NoCar).
        const int M_SIM = 8, M_TABLE = 9, M_KEYS = 10;
        const int NoCar = 255;
        const int SimFinal = 1, SimParked = 2;   // drapeaux de M_SIM : rendue (fiable), instantane de l'hote (garee)

        // Etat de la simulation porte d'un conducteur a l'autre : { automate, debut du nom de l'objet ("" : tous),
        // variables float... }. Les commandes (lignes FirstControlRow et suivantes) : seulement dans l'etat rendu.
        static readonly string[][] SimVars = {
            new[] { "Power", "Electricity", "Charge" },                               // batterie (SORBET, taxi...)
            new[] { "Electrics", "", "Charge" },                                      // batterie (CORRIS : Systems/Electrics)
            new[] { "Data", "VINP_Battery", "Charge" },                               // batterie montee (CORRIS)
            new[] { "Cooling", "", "Temp", "Temp2", "EngineTemp", "CoolantTemp" },    // temperature du moteur
            new[] { "OperatingTemp", "", "Temp", "Temp2" },
            new[] { "Priming", "", "FuelChamber", "Priming" },                        // amorcage, carburateur noye
            new[] { "Starter", "", "PlugHeat" },                                      // bougies de prechauffage (diesels)
            new[] { "Air Pressure", "", "Pressure" },                                 // freins a air (GIFU)
            new[] { "Use", "ButtonChoke", "Choke" },                                  // tirette de starter (commande)
            new[] { "FuelLine", "", "Choke" },
            new[] { "Mixture", "", "Choke", "Choke2" },
        };
        const int FirstControlRow = 8;

        static readonly List<Car> cars = new List<Car>();
        static readonly List<Car> byNet = new List<Car>();         // numero reseau -> voiture d'ici (null : absente ici)
        static readonly List<string> netKeys = new List<string>(); // numero reseau -> cle (table de l'hote)
        static readonly HashSet<int> missingLogged = new HashSet<int>();
        static bool scanned, tableSeen, inCarWas, curVehicleLooked;
        static float scanAt = -1, nextScan, nextFast, nextSlow, nextSim, nextParkedSim, nextKeys, lastMissingScan;
        static int generation;
        static FsmString curVehicle;
        public static int LocalDriving = -1;    // rang local de la voiture conduite ici
        public static string LocalDrivingKey { get { return LocalDriving >= 0 && LocalDriving < cars.Count ? cars[LocalDriving].Key : null; } }
        static int owned = -1, ownedTick;        // voiture quittee moteur tournant : on en garde la main
        // Voiture garee poussee par le joueur local (main "Hand Push") : on en prend la main (owned) tant qu'on pousse et
        // qu'elle roule encore (8 s au plus apres la poussee), puis on la rend. Avant : seul l'hote pouvait pousser ; chez
        // un invite, l'hote la recalait toutes les 2 s (voiture qui se teleporte en arriere ; retour d'un joueur, 08/10).
        static int pushCar = -1;
        static float nextKeyLook;
        static float pushUntil, nextMoveLook;
        // Remorques attelees (FLATBED au KEKMET...) : un joint relie deux voitures (hors cordes de Tow). Celui qui conduit
        // le vehicule de tete envoie la remorque 10 fois/s (etat 2) : ailleurs elle suit en copie, l'hote ne la recale
        // plus. Avant : l'hote la recalait toutes les 2 s alors qu'elle etait attelee au tracteur d'un invite (a-coups,
        // sauts), et chez les autres elle etait trainee par une copie en retard (retour d'un joueur, 08/10 : « la
        // remorque a bois est buguee »). Detelee (ou plus conduite ici) : derniere pose fiable, rendue.
        static readonly List<KeyValuePair<int, int>> hitches = new List<KeyValuePair<int, int>>();   // (voiture, voiture) relies, rangs locaux
        static readonly HashSet<int> hitchSent = new HashSet<int>();   // remorques envoyees d'ici (rangs locaux)
        static float nextHitchScan;
        public static bool HitchCarried(int index) { return hitchSent.Contains(index); }

        static void HitchScan(float now)
        {
            if (now < nextHitchScan) return;
            nextHitchScan = now + 1f;
            var found = new List<KeyValuePair<int, int>>();
            foreach (Car a in cars)
            {
                if (a.Body == null || !a.Body.gameObject.activeInHierarchy) continue;
                foreach (Joint j in a.Body.GetComponentsInChildren<Joint>())
                {
                    if (j == null || j.connectedBody == null || j is SpringJoint || Tow.IsRope(j)) continue;
                    Car b = null;
                    foreach (Car x in cars) if (x != a && x.Body != null && (x.Body == j.connectedBody || j.connectedBody.transform.IsChildOf(x.T))) { b = x; break; }
                    if (b == null) continue;
                    bool dup = false;
                    foreach (KeyValuePair<int, int> kv in found) if ((kv.Key == a.Index && kv.Value == b.Index) || (kv.Key == b.Index && kv.Value == a.Index)) dup = true;
                    if (!dup) found.Add(new KeyValuePair<int, int>(a.Index, b.Index));
                }
            }
            foreach (KeyValuePair<int, int> kv in found)
            {
                bool known = false;
                foreach (KeyValuePair<int, int> h in hitches) if (h.Key == kv.Key && h.Value == kv.Value) known = true;
                if (!known) Log.Info("attelage : " + cars[kv.Key].Key + " <-> " + cars[kv.Value].Key);
            }
            foreach (KeyValuePair<int, int> h in hitches)
            {
                bool still = false;
                foreach (KeyValuePair<int, int> kv in found) if (h.Key == kv.Key && h.Value == kv.Value) still = true;
                if (!still) Log.Info("attelage defait : " + cars[h.Key].Key + " <-> " + cars[h.Value].Key);
            }
            hitches.Clear();
            hitches.AddRange(found);
        }

        // 20 fois/s : la remorque attelee au vehicule qu'on conduit (sauf si un autre la conduit).
        static void HitchTick(float now)
        {
            var want = new HashSet<int>();
            if (LocalDriving >= 0)
                foreach (KeyValuePair<int, int> h in hitches)
                {
                    int other = h.Key == LocalDriving ? h.Value : h.Value == LocalDriving ? h.Key : -1;
                    if (other < 0 || other == owned) continue;
                    Car t = cars[other];
                    if (t.Body == null || Remote(t, now) || t.Net < 0) continue;
                    want.Add(other);
                }
            foreach (int i in want) Send(cars[i], 2);
            foreach (int i in hitchSent)
                if (!want.Contains(i) && cars[i].Body != null) { Send(cars[i], 0); cars[i].AuthorityUntil = now + 5f; Log.Info("attelage : " + cars[i].Key + " rendue"); }
            hitchSent.Clear();
            foreach (int i in want) hitchSent.Add(i);
        }
        public static string LocalDrivingName { get { return LocalDriving >= 0 && LocalDriving < cars.Count ? cars[LocalDriving].Name : null; } }
        public static Transform LocalDrivingRoot { get { return LocalDriving >= 0 && LocalDriving < cars.Count ? cars[LocalDriving].T : null; } }
        // Voiture menee d'ici : conduite, ou quittee moteur tournant (on en garde la main).
        public static Transform LocalLeadRoot { get { int i = LocalDriving >= 0 ? LocalDriving : owned; return i >= 0 && i < cars.Count ? cars[i].T : null; } }

        // Change quand la liste des voitures d'ici change (voiture trouvee, corps recree) : CarDoors, CarVisuals et
        // Seats relevent alors la leur.
        public static int Generation { get { return generation; } }

        public static void OnLevelLoaded()
        {
            cars.Clear(); under.Clear(); byNet.Clear(); netKeys.Clear(); missingLogged.Clear();
            scanned = false; tableSeen = false; inCarWas = false; curVehicle = null; curVehicleLooked = false;
            LocalDriving = -1; owned = -1; moving = null;
            scanAt = PlayerSync.InGame ? Time.realtimeSinceStartup + 3f : -1;
            tStep = 0; tRec = 0;
        }

        // ---------------------------------------------------------------- quelles voitures
        // Corps de voiture : CarDynamics a la racine de la scene, ou voiture de joueur (AxisCarController : pas la
        // circulation, menee par MobileCarController) rangee sous une autre racine (taxi sous JOBS/TAXIJOB).
        public static bool IsCarBody(Rigidbody rb)
        {
            if (rb == null) return false;
            if (rb.GetComponent("CarDynamics") == null) return IsModVehicle(rb);
            return rb.transform.parent == null || rb.GetComponent<AxisCarController>() != null;
        }

        // Vehicules de mods a physique maison (pas de CarDynamics) : motoneige Okkelmo Poro (racine "PORO" : Rigidbody, skis
        // relies par ConfigurableJoint ; le mod range le joueur sous la racine et met PlayerCurrentVehicle a "Poro"). Avant,
        // jamais suivis : chacun voyait le sien a sa place, immobile chez les autres. Suivis comme les voitures (conducteur
        // qui envoie, copie cinematique ailleurs, recalage de l'hote garee) ; au volant : joueur range sous la racine avec
        // un vehicule courant. Autres : [Coop] VehiculesMods=NOM1,NOM2 (noms des objets racine).
        static HashSet<string> modNames;
        static bool IsModVehicle(Rigidbody rb)
        {
            if (rb.transform.parent != null) return false;
            if (modNames == null)
            {
                modNames = new HashSet<string> { "PORO" };
                foreach (string n in Config.Get("Coop", "VehiculesMods", "").Split(',')) if (n.Trim().Length > 0) modNames.Add(n.Trim());
            }
            return modNames.Contains(rb.name);
        }

        // Voiture qui porte 't' (lui-meme ou un parent) : celles connues d'abord (sans GetComponent), sinon en
        // remontant les parents (voiture pas encore relevee, inactive). null : pas dans une voiture.
        public static Transform CarRoot(Transform t)
        {
            Car c = CarOf(t);
            if (c != null) return c.T;
            for (Transform p = t; p != null; p = p.parent)
            {
                Rigidbody rb = p.GetComponent<Rigidbody>();
                if (rb != null && IsCarBody(rb)) return p;
            }
            return null;
        }

        // Chemin de 't' sous la voiture "/Doors/DoorFront(leftx)..." ("" : la voiture elle-meme). Pour une voiture a
        // la racine, c'est le chemin complet sans le nom de la voiture (cles inchangees).
        public static string RelPath(Transform car, Transform t)
        {
            string p = "";
            for (Transform x = t; x != null && x != car; x = x.parent) p = "/" + x.name + p;
            return p;
        }

        // Cle (reseau) de la voiture qui porte 't' (null : pas une voiture connue).
        public static string KeyOf(Transform t) { Car c = CarOf(t); return c != null ? c.Key : null; }

        // Voitures connues ici, dans l'ordre local (corps null : detruit, en attente d'un nouveau).
        public static int LocalCount { get { return cars.Count; } }
        public static Rigidbody LocalBody(int i) { return i >= 0 && i < cars.Count ? cars[i].Body : null; }
        public static string LocalKey(int i) { return i >= 0 && i < cars.Count ? cars[i].Key : null; }

        static Car CarOf(Transform t)
        {
            for (; t != null; t = t.parent)
                for (int i = 0; i < cars.Count; i++)
                    if (ReferenceEquals(cars[i].T, t)) return cars[i];
            return null;
        }

        static Car Named(string name)
        {
            foreach (Car c in cars) if (c.Name == name || c.Key == name) return c;
            foreach (Car c in cars) if (c.Name.StartsWith(name)) return c;
            return null;
        }

        static int CompareBodies(Rigidbody a, Rigidbody b)
        {
            int k = string.CompareOrdinal(a.name, b.name);
            if (k != 0) return k;
            k = string.CompareOrdinal(Recon.Path(a.transform), Recon.Path(b.transform));
            if (k != 0) return k;
            k = a.position.x.CompareTo(b.position.x);
            return k != 0 ? k : a.position.z.CompareTo(b.position.z);
        }

        static void Scan()
        {
            scanned = true;
            float now = Time.realtimeSinceStartup;
            nextScan = now + 20f;
            var found = new List<Rigidbody>();
            foreach (Rigidbody rb in Object.FindObjectsOfType<Rigidbody>())
            {
                if (!IsCarBody(rb)) continue;
                bool known = false;
                foreach (Car c in cars) if (ReferenceEquals(c.Body, rb)) { known = true; break; }
                if (!known) found.Add(rb);
            }
            // Pieces montees depuis (batterie de la CORRIS) : variables de simulation revues.
            foreach (Car c in cars) if (c.Body != null) FindSims(c);
            if (found.Count == 0) return;
            found.Sort(CompareBodies);
            var added = new List<string>();
            foreach (Rigidbody rb in found)
            {
                // Corps recree (cyclomoteur reapparu) : la voiture de meme nom dont le corps a disparu le reprend.
                Car dead = null;
                foreach (Car c in cars) if (c.Name == rb.name && c.Body == null) { dead = c; break; }
                Car car = Make(rb);
                if (dead != null)
                {
                    car.Index = dead.Index; car.Net = dead.Net; car.Key = dead.Key;
                    cars[car.Index] = car;
                    if (car.Net >= 0 && car.Net < byNet.Count) byNet[car.Net] = car;
                    if (owned == car.Index) owned = -1;
                    added.Add(car.Key + " (nouveau corps)");
                    continue;
                }
                int rank = 0;
                foreach (Car c in cars) if (c.Name == rb.name) rank++;
                car.Index = cars.Count;
                car.Key = rank == 0 ? rb.name : rb.name + "#" + rank;
                cars.Add(car);
                if (Session.IsHost) { if (byNet.Count < NoCar) { car.Net = byNet.Count; byNet.Add(car); netKeys.Add(car.Key); } }
                else Bind(car);
                added.Add(car.Key + (car.Drive != null ? "" : "(?)") + (car.T.parent != null ? " [" + Recon.Path(car.T.parent) + "]" : "") + (car.Sims.Length > 0 ? " sim " + car.Sims.Length : ""));
            }
            generation++;
            Log.Info("voitures : " + string.Join(", ", added.ToArray()) + " (" + cars.Count + " ici)");
            if (Session.IsHost) SendTable(null); else SendKeys();
        }

        static Car Make(Rigidbody rb)
        {
            GameObject go = rb.gameObject;
            var car = new Car { Name = go.name, T = go.transform, Body = rb,
                                Dt = go.GetComponent<Drivetrain>(), Wheels = go.GetComponentsInChildren<Wheel>(true),
                                Axis = go.GetComponent<AxisCarController>(), Sound = go.GetComponent<SoundController>(),
                                Mod = go.GetComponent("CarDynamics") == null };
            if (car.Mod)
            {
                car.ModDt = go.GetComponent("SnowmobileDrivetrain");
                if (car.ModDt != null)
                {
                    var bf = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                    car.ModRpm = car.ModDt.GetType().GetProperty("CurrRpm", bf);
                    car.ModRun = car.ModDt.GetType().GetProperty("IsRunning", bf);
                }
                car.ModCtrl = go.GetComponent("SnowmobileController") as Behaviour;
            }
            car.WheelRot = new float[car.Wheels.Length];
            var snd = new List<GameObject>();
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                if (t.name == "Sounds")
                    foreach (Transform s in t) if (s.GetComponent<AudioSource>() != null && snd.Count < 14) snd.Add(s.gameObject);   // (bits 14-15 : klaxon, moteur en marche)
            car.SoundObjs = snd.ToArray();
            car.SoundObjsWas = new bool[snd.Count];
            car.SoundPitch = new float[snd.Count];
            car.SoundVol = new float[snd.Count];
            foreach (PlayMakerFSM f in go.GetComponentsInChildren<PlayMakerFSM>(true))
                if (f.FsmName == "PlayerTrigger" && f.gameObject.name.StartsWith("DriveTrigger")) { car.Drive = f; break; }
            foreach (PlayMakerFSM f in go.GetComponentsInChildren<PlayMakerFSM>(true))
                if (f.FsmName == "Starter" && f.Fsm.GetState("Running") != null) { car.Starter = f; break; }
            foreach (AudioSource a in go.GetComponentsInChildren<AudioSource>(true))
                if (a.gameObject.name == "CarHorn") { car.Horn = a.gameObject; break; }
            foreach (PlayMakerFSM f in go.GetComponentsInChildren<PlayMakerFSM>(true))
                if (f.FsmName == "Data" && f.gameObject.name.StartsWith("HeatSource")) { car.Heat = f.FsmVariables.FindFsmFloat("Temperature"); break; }
            FindSims(car);
            return car;
        }

        // Variables de simulation de la voiture (SimVars) ; cle : chemin sous la voiture, automate, variable (+ rang).
        static void FindSims(Car c)
        {
            var list = new List<Sim>();
            var seen = new Dictionary<string, int>();
            foreach (PlayMakerFSM f in c.Body.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                for (int row = 0; row < SimVars.Length; row++)
                {
                    string[] sv = SimVars[row];
                    if (f.FsmName != sv[0] || (sv[1].Length > 0 && !f.gameObject.name.StartsWith(sv[1]))) continue;
                    for (int i = 2; i < sv.Length; i++)
                    {
                        FsmFloat v = f.FsmVariables.FindFsmFloat(sv[i]);
                        if (v == null) continue;
                        string key = RelPath(c.T, f.transform) + ":" + f.FsmName + "." + sv[i];
                        int k; seen.TryGetValue(key, out k); seen[key] = k + 1;
                        if (k > 0) key += "#" + k;
                        list.Add(new Sim { Key = key, Hash = Hash(key), Var = v, Control = row >= FirstControlRow });
                    }
                }
            }
            c.Sims = list.ToArray();
        }

        static uint Hash(string s)
        {
            uint h = 2166136261;
            foreach (char ch in s) { h ^= ch; h *= 16777619; }
            return h;
        }

        // ---------------------------------------------------------------- table des numeros
        // Invite : rattache la voiture a son numero chez l'hote (meme cle).
        static void Bind(Car car)
        {
            for (int n = 0; n < netKeys.Count; n++)
                if (netKeys[n] == car.Key) { car.Net = n; byNet[n] = car; return; }
        }

        // Hote : la table (numero, cle) a 'to', ou a tous (null). Par messages de moins de 1000 octets.
        static void SendTable(Peer to)
        {
            if (!Session.Active || Session.T == null) return;
            int i = 0;
            while (i < netKeys.Count)
            {
                var w = new NetWriter(Msg.Vehicle).U8(Session.LocalId).U8(NoCar).U8(M_TABLE);
                int start = i, n = 0, len = 0;
                while (i < netKeys.Count && n < 200 && (n == 0 || len + netKeys[i].Length < 900)) { len += netKeys[i].Length + 3; n++; i++; }
                w.U8(n);
                for (int k = start; k < start + n; k++) w.U8(k).Str(netKeys[k]);
                if (to == null) Session.Broadcast(w, true);
                else Session.T.SendReliable(to, w.ToArray());
            }
        }

        // Invite : ses cles a l'hote, qui repond par sa table (et note les differences).
        static void SendKeys()
        {
            if (!Session.Active || Session.IsHost) return;
            nextKeys = Time.realtimeSinceStartup + 5f;
            var w = new NetWriter(Msg.Vehicle).U8(Session.LocalId).U8(NoCar).U8(M_KEYS);
            int n = 0, len = 0;
            foreach (Car c in cars) { if (len + c.Key.Length > 900) break; len += c.Key.Length + 2; n++; }
            w.U8(n);
            for (int i = 0; i < n; i++) w.Str(cars[i].Key);
            Session.SendToHost(w, true);
        }

        static void OnTable(NetReader r)
        {
            if (Session.IsHost) return;
            int n = r.U8();
            for (int i = 0; i < n; i++)
            {
                int net = r.U8();
                string key = r.Str();
                while (netKeys.Count <= net) { netKeys.Add(null); byNet.Add(null); }
                netKeys[net] = key;
                Car c = null;
                foreach (Car x in cars) if (x.Key == key) { c = x; break; }
                byNet[net] = c;
                if (c != null) c.Net = net;
            }
            tableSeen = true;
            var missing = new List<string>();
            for (int i = 0; i < netKeys.Count; i++) if (netKeys[i] != null && byNet[i] == null) missing.Add(netKeys[i]);
            var extra = new List<string>();
            foreach (Car c in cars) if (c.Net < 0) extra.Add(c.Key);
            Log.Info("voitures : table de l'hote, " + netKeys.Count + " numeros" + (missing.Count > 0 ? " ; absentes ici (pas encore actives ?) : " + string.Join(", ", missing.ToArray()) : "")
                     + (extra.Count > 0 ? " ; inconnues de l'hote (non suivies) : " + string.Join(", ", extra.ToArray()) : ""));
            // Voitures de l'hote pas encore vues ici : nouveau releve bientot.
            if (missing.Count > 0) nextScan = Mathf.Min(nextScan, Time.realtimeSinceStartup + 10f);
        }

        static void OnKeys(Peer from, NetReader r)
        {
            if (!Session.IsHost) return;
            var theirs = new HashSet<string>();
            for (int i = 0, n = r.U8(); i < n; i++) theirs.Add(r.Str());
            if (!scanned) return;   // (il redemandera)
            var notThere = new List<string>();
            foreach (string k in netKeys) if (!theirs.Contains(k)) notThere.Add(k);
            var notHere = new List<string>();
            foreach (string k in theirs) if (!netKeys.Contains(k)) notHere.Add(k);
            if (notThere.Count + notHere.Count == 0) Log.Info("voitures : liste de " + from + " identique (" + theirs.Count + ")");
            else Log.Warn("voitures : liste de " + from + " differente -- absentes chez lui : " + string.Join(", ", notThere.ToArray()) + " ; absentes ici : " + string.Join(", ", notHere.ToArray()));
            SendTable(from);
        }

        // ---------------------------------------------------------------- boucle
        public static void Update()
        {
            if (!Session.Active || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if (!scanned) { if (scanAt > 0 && now >= scanAt) Scan(); return; }
            // Joueur monte dans une voiture que la liste ne connait pas encore (taxi tout juste active) : releve.
            if (!curVehicleLooked) { curVehicleLooked = true; curVehicle = FsmVariables.GlobalVariables.FindFsmString("PlayerCurrentVehicle"); }
            bool inCar = curVehicle != null && !string.IsNullOrEmpty(curVehicle.Value);
            if (inCar && !inCarWas && LocalDriving < 0) nextScan = Mathf.Min(nextScan, now + 0.5f);
            inCarWas = inCar;
            if (now >= nextScan) Scan();
            if (!Session.IsHost && !tableSeen && now >= nextKeys) SendKeys();

            int driving = -1;
            foreach (Car c in cars)
                if (c.Drive != null && c.Drive.ActiveStateName == "Player in car") { driving = c.Index; break; }
            if (driving < 0 && inCar)
            {
                Transform pl = Game.PlayerT;
                if (pl != null && pl.parent != null)
                    foreach (Car c in cars)
                        if (c.Mod && c.Body != null && pl.IsChildOf(c.T)) { driving = c.Index; break; }
            }
            if (driving != LocalDriving)
            {
                Log.Info(driving >= 0 ? "au volant de " + cars[driving].Key + (cars[driving].Net < 0 ? " (absente de la table de l'hote : non suivie)" : "") : "sorti de " + cars[LocalDriving].Key);
                if (LocalDriving >= 0)
                {
                    Car was = cars[LocalDriving];
                    if (EngineRunning(was)) { owned = LocalDriving; Log.Info("moteur laisse tournant : " + was.Key + " reste a nous"); }
                    else Release(was, now);   // derniere position et etat du moteur, sans conducteur
                }
                if (driving >= 0) owned = -1;
                LocalDriving = driving;
            }
            // Moteur demarre ici sans etre au volant (cle tournee par un passager, ou de l'exterieur) : personne ne la
            // menait, chacun faisait tourner son moteur (melange, batterie, regime a lui) et il calait chez l'un, tournait
            // chez l'autre. Celui qui a tourne la cle en garde la main comme un moteur laisse tournant (etat 2).
            if (LocalDriving < 0 && owned < 0 && now >= nextKeyLook)
            {
                nextKeyLook = now + 0.5f;
                foreach (Car c in cars)
                    if (c.Body != null && ((c.Starter != null && c.Starter.ActiveStateName == "Running") || EngineRunning(c)) && !Remote(c, now) && c.RemoteBy < 0
                        && c.Net >= 0 && Jobs.KeyTurnedHere(c.T, 30f))
                    { owned = c.Index; Log.Info("moteur demarre ici sans conducteur : " + c.Key + " reste a nous"); break; }
            }

            if (now >= nextFast)
            {
                nextFast = now + 0.05f;
                if (LocalDriving < 0) PushTick(now);
                HitchScan(now);
                HitchTick(now);
                if (LocalDriving >= 0) Send(cars[LocalDriving], 1);
                else if (owned >= 0 && (++ownedTick & 1) == 0)   // 10 fois/s
                {
                    Car o = cars[owned];
                    bool pushed = o.Index == pushCar && o.Body != null && (now < pushUntil || (o.Body.velocity.sqrMagnitude > 0.09f && now < pushUntil + 8f));
                    if (o.RemoteBy >= 0 && o.RemoteBy != Session.LocalId) { owned = -1; pushCar = -1; }     // un autre l'a prise
                    else if (EngineRunning(o) || pushed) Send(o, 2);
                    else { Release(o, now); Log.Info((o.Index == pushCar ? "poussee finie : " : "moteur coupe : ") + o.Key + " rendue"); owned = -1; pushCar = -1; }
                }
            }
            // Etat de la simulation de ce qu'on fait rouler : une fois par seconde.
            if (now >= nextSim)
            {
                nextSim = now + 1f;
                if (LocalDriving >= 0) SendSim(cars[LocalDriving], 0, false);
                if (owned >= 0) SendSim(cars[owned], 0, false);
            }
            if (Session.IsHost && now >= nextSlow)
            {
                nextSlow = now + 2f;
                foreach (Car c in cars)
                    if (c.Index != LocalDriving && c.Index != owned && c.RemoteBy < 0 && c.Body != null && c.Body.gameObject.activeInHierarchy && !Tow.Carries(c.Index) && !hitchSent.Contains(c.Index)) Send(c, 0);
            }
            // Hote : etat de la simulation des voitures garees (personne ne les fait rouler), toutes les 10 s.
            if (Session.IsHost && now >= nextParkedSim && Session.RemoteCount > 0)
            {
                nextParkedSim = now + 10f;
                foreach (Car c in cars)
                    if (c.Index != LocalDriving && c.Index != owned && !Remote(c, now) && c.Body != null && c.Body.gameObject.activeInHierarchy) SendSim(c, SimParked, false);
            }

            HotTick();
            foreach (Car c in cars)
            {
                if (c.Body == null) continue;
                bool remote = Remote(c, now);
                if (!remote && c.RemoteBy >= 0 && now - c.LastRemote >= 1.5f) { c.RemoteBy = -1; c.RemoteDriver = -1; }
                SetKinematic(c, remote);
                Ride(c, remote && Seats.SeatedCar == c.T);
                if (remote)
                {
                    if (c.Framewise && c.Body.gameObject.activeInHierarchy) FollowFrame(c);
                    if (now >= c.NextJoints) ProtectJoints(c);   // pieces montees entre-temps
                    Animate(c);
                    if (now >= c.NextLog) { c.NextLog = now + 5f; Log.Info(c.Key + (c.RemoteDriver >= 0 ? " conduite par #" + c.RemoteDriver : " moteur tournant chez #" + c.RemoteBy) + " : " + c.Body.position.ToString("F1") + ", regime " + (c.Dt != null ? c.Dt.rpm.ToString("F0") : "?") + ", chaleur " + (c.Heat != null ? c.Heat.Value.ToString("F1") : "?") + " (recue " + c.RemoteHeat.ToString("F1") + ")" +  (Config.GetInt("Test", "JournalSons", 0) != 0 ? " | " + SoundDiag(c) : "")); }
                }
            }
        }

        // Poussee : la voiture la plus proche du joueur (a moins de 1,5 m de sa carrosserie), garee ici (pas une copie).
        // Aussi sans le geste de poussee : un vehicule gare qui bouge a cote du joueur (mobylette soulevee ou tiree a la main,
        // retour d'un joueur, 09/10 : « je deplace la Jonnez, je monte dessus, elle revient a sa place ») -- c'est nous qui le
        // bougeons, les autres suivent ; sa derniere pose part quand il s'arrete (Release).
        static void PushTick(float now)
        {
            bool gesture = Gestures.Pushing;
            if (!gesture && now < nextMoveLook) return;
            if (!gesture) nextMoveLook = now + 0.25f;
            GameObject pl = GameObject.Find("PLAYER");
            if (pl == null) return;
            Vector3 p = pl.transform.position + Vector3.up * 0.8f;
            Car best = null;
            float bd = 1.5f * 1.5f;
            foreach (Car c in cars)
            {
                if (c.Body == null || c.Kinematic || !c.Body.gameObject.activeInHierarchy) continue;
                if (!gesture && (c.Body.velocity.sqrMagnitude < 0.16f || c.RemoteBy >= 0)) continue;   // (sans geste : seulement ce qui bouge, a personne)
                float d = (c.Body.ClosestPointOnBounds(p) - p).sqrMagnitude;
                if (d < bd) { bd = d; best = c; }
            }
            if (best == null) return;
            pushUntil = now + 1.5f;
            if (pushCar == best.Index && owned == best.Index) return;
            if (owned >= 0 && owned != best.Index) return;   // (on garde deja une autre voiture : moteur tournant)
            pushCar = best.Index;
            owned = best.Index;
            Log.Info("poussee : " + best.Key + " suit notre poussee chez les autres");
        }

        // Voiture rendue (conducteur sorti moteur arrete, ou moteur coupe) : derniere pose et etat de la simulation
        // (fiable) ; l'instantane de l'hote ne l'ecrase pas tout de suite.
        static void Release(Car c, float now)
        {
            Send(c, 0);
            SendSim(c, SimFinal, true);
            c.AuthorityUntil = now + 5f;
        }

        // Apres la logique du jeu : la chaleur de l'habitacle de la copie est celle de chez le conducteur.
        public static void LateUpdate()
        {
            if (!scanned) return;
            float now = Time.realtimeSinceStartup;
            foreach (Car c in cars)
                if (c.Heat != null && !float.IsNaN(c.RemoteHeat) && c.RemoteBy >= 0 && now - c.LastRemote < 1.5f && c.Index != LocalDriving)
                    c.Heat.Value = c.RemoteHeat;
            // Klaxon de la copie : celui du conducteur (apres la logique du jeu) ; rendu a son etat d'avant ensuite.
            foreach (Car c in cars)
            {
                if (c.Horn == null) continue;
                bool copy = c.Kinematic && Remote(c, now);
                if (copy)
                {
                    if (!c.HornSet) { c.HornSet = true; c.HornWas = c.Horn.activeSelf; }
                    if (c.Horn.activeSelf != c.HornRemote) c.Horn.SetActive(c.HornRemote);
                }
                else if (c.HornSet) { c.HornSet = false; c.Horn.SetActive(c.HornWas); }
            }
            // Objets transportes dans une copie : places sur la pose affichee de la voiture (apres sa physique).
            Props.LateUpdate();
        }

        // Copie conduite ailleurs (cinematique ici, suit les messages de celui qui la fait rouler).
        static bool Remote(Car c, float now)
        {
            return c.RemoteBy >= 0 && now - c.LastRemote < 1.5f && c.Index != LocalDriving && c.Index != owned;
        }

        // ---------------------------------------------------------------- objets dans les voitures
        // Numero d'une voiture : son numero reseau (table de l'hote), le meme chez tous (-1 : pas une voiture,
        // ou absente de la table).
        public static int Count { get { return byNet.Count; } }
        static Car ByNet(int index) { return index >= 0 && index < byNet.Count ? byNet[index] : null; }
        public static int CarIndex(Rigidbody body)
        {
            if (body == null) return -1;
            foreach (Car c in cars) if (ReferenceEquals(c.Body, body)) return c.Net;
            return -1;
        }
        public static Rigidbody CarBody(int index) { Car c = ByNet(index); return c != null ? c.Body : null; }

        // Voiture ou se trouve le joueur local (au volant, ou assis en passager) ; null : a pied.
        public static Rigidbody LocalRideBody()
        {
            if (LocalDriving >= 0 && LocalDriving < cars.Count) return cars[LocalDriving].Body;
            Transform st = Seats.SeatedCar;
            if (st != null) foreach (Car c in cars) if (c.T == st) return c.Body;
            return null;
        }

        // Conduite ici, ou quittee moteur tournant (on en garde la main) : on fait autorite dessus.
        public static bool DrivenHere(int index) { Car c = ByNet(index); return c != null && (c.Index == LocalDriving || c.Index == owned || hitchSent.Contains(c.Index)); }   // (remorque tiree d'ici comprise)

        // Copie ici d'une voiture qu'un autre fait rouler.
        public static bool IsCopy(int index) { Car c = ByNet(index); return c != null && Remote(c, Time.realtimeSinceStartup); }

        // Qui fait autorite sur la voiture : nous si on la conduit (ou moteur laisse tournant), le joueur qui
        // la fait rouler chez lui, sinon l'hote (voiture garee).
        public static int Authority(int index)
        {
            Car c = ByNet(index);
            if (c == null) return 0;
            // (remorque attelee au vehicule qu'on conduit : a nous aussi -- benne, bois, compteur ; avant : a l'hote, et quand un
            // invite tirait le plateau, personne n'avait la benne ni le compteur de bois)
            if (c.Index == LocalDriving || c.Index == owned || hitchSent.Contains(c.Index)) return Session.LocalId;
            return Remote(c, Time.realtimeSinceStartup) ? c.RemoteBy : 0;
        }
        public static int Authority(Rigidbody car) { return Authority(CarIndex(car)); }

        // Vitesse de la voiture telle qu'on la voit ici (copie : celle recue, le corps cinematique n'en a pas).
        public static Vector3 CarVelocity(int index)
        {
            Car c = ByNet(index);
            if (c == null || c.Body == null) return Vector3.zero;
            return c.Kinematic ? c.Vel : c.Body.velocity;
        }

        // Regime du moteur de la voiture qui porte 't', tel que chez celui qui la fait tourner : le notre si on la
        // conduit ou en garde la main, celui recu sur une copie (recopie aussi a chaque image dans le Drivetrain
        // coupe de la copie : les automates qui le lisent, pompe de la fosse septique comprise, le voient), le
        // regime local d'une voiture garee. -1 : pas une voiture connue.
        public static float EngineRpm(Transform t)
        {
            Car c = CarOf(t);
            if (c == null) return -1f;
            if (Remote(c, Time.realtimeSinceStartup)) return c.Rpm;
            return c.Dt != null ? c.Dt.rpm : 0f;
        }

        // Cette machine fait-elle tourner la simulation qui compte pour la voiture qui porte 't' ? Oui si on la
        // conduit ou en garde la main ; non sur la copie d'une voiture qu'un autre fait rouler ; garee : l'hote.
        public static bool SimAuthority(Transform t)
        {
            Car c = CarOf(t);
            if (c == null) return Session.IsHost;
            if (c.Index == LocalDriving || c.Index == owned) return true;
            if (Remote(c, Time.realtimeSinceStartup)) return false;
            return Session.IsHost;
        }

        // Voiture sous un objet pose (coffre, banquette, plateau) : court rayon vers le bas depuis son centre,
        // premiere surface solide d'une voiture (son corps, ou une piece articulee dessus comme le hayon). Les
        // autres objets en travers sont ignores (sac pose sur une caisse). Resultat garde 0,5 s par objet.
        // null : pas dans une voiture.
        // Pieces de la voiture elle-meme (corps sous elle dans la hierarchie : portieres, hayon, reservoir, tete du
        // conducteur, recu de l'imprimante du taxi...) : la voiture, sans rayon -- leur pose au repos part dans
        // son repere et l'hote ne les recale pas quand un autre la conduit (Props, Authority). Mais jamais un
        // chargement : null pour celle qu'on fait rouler ici (Props.Ride les collerait, cinematiques, chez les
        // autres) et pendant son propre recalage d'un coup (MoveCargo : elles suivent deja son transform).
        // Les voitures a la racine en etaient deja exclues par Props (racine == voiture) ; pas le taxi, sous JOBS.
        class Under { public float Until; public Rigidbody Car; public Car Own; }
        static readonly Dictionary<Rigidbody, Under> under = new Dictionary<Rigidbody, Under>();
        static Car moving;   // voiture replacee d'un coup en ce moment (MoveCargo)

        public static Rigidbody CarUnder(Rigidbody item)
        {
            if (item == null || !scanned) return null;
            float now = Time.realtimeSinceStartup;
            Under u;
            if (under.TryGetValue(item, out u) && now < u.Until) return UnderFor(u);
            if (u == null)
            {
                if (under.Count > 512) under.Clear();   // objets detruits depuis
                u = new Under();
                under[item] = u;
            }
            u.Until = now + 0.5f;
            u.Car = null;
            u.Own = null;
            // Aucune voiture a moins de 8 m : pas de rayon (la plupart des objets du monde, recalage de l'hote).
            Vector3 at = item.position;
            bool near = false;
            foreach (Car c in cars) if (c.Body != null && (c.Body.position - at).sqrMagnitude < 64f) { near = true; break; }
            if (!near) return null;
            Car own = CarOf(item.transform);
            if (own != null && own.Body != null && !ReferenceEquals(own.Body, item)) { u.Own = own; u.Car = own.Body; return UnderFor(u); }
            // Attele a une voiture par une articulation (fendeuse au relevage du KEKMET...) : comme pose dedans -- c'est
            // celui qui la conduit qui le transporte (Props.Ride), l'hote ne le recale plus. Retour d'un joueur, 10/10 :
            // « attelee par un invite, la fendeuse rentre dans la remorque et s'envole » (l'hote la recalait a sa pose,
            // prise sur sa copie du tracteur en retard, contre l'articulation).
            Car jc = JointCar(item, at);
            if (jc != null) { u.Car = jc.Body; return u.Car; }
            float best = float.MaxValue;
            foreach (RaycastHit h in Physics.RaycastAll(item.worldCenterOfMass + Vector3.up * 0.25f, Vector3.down, 0.85f, ~0))
            {
                Collider col = h.collider;
                if (col == null || col.isTrigger || h.distance >= best) continue;
                Rigidbody rb = col.attachedRigidbody;
                if (rb == null || rb == item) continue;
                Car hc = CarOf(rb.transform);
                if (hc != null && hc.Body != null) { best = h.distance; u.Car = hc.Body; }
            }
            return u.Car;
        }

        // Voiture reliee a l'objet par une articulation (pas une corde de Tow), dans un sens ou dans l'autre.
        static Car JointCar(Rigidbody item, Vector3 at)
        {
            if (Config.GetInt("Test", "SansAttelageObjets", 0) != 0) return null;   // (essais : comme avant)
            foreach (Joint j in item.GetComponents<Joint>())
            {
                if (j == null || j.connectedBody == null || Tow.IsRope(j)) continue;
                Car c = CarOf(j.connectedBody.transform);
                if (c != null && c.Body != null && !ReferenceEquals(c.Body, item)) return c;
            }
            foreach (Car c in cars)
            {
                if (c.Body == null || ReferenceEquals(c.Body, item) || (c.Body.position - at).sqrMagnitude > 100f) continue;
                foreach (Joint j in c.Body.GetComponentsInChildren<Joint>())
                    if (j != null && ReferenceEquals(j.connectedBody, item) && !Tow.IsRope(j)) return c;
            }
            return null;
        }

        // Piece de la voiture : pas un chargement de celle qu'on fait rouler ici, ni de celle qu'on replace (hors cache :
        // monter au volant compte tout de suite).
        static Rigidbody UnderFor(Under u)
        {
            Car o = u.Own;
            if (o != null && (ReferenceEquals(o, moving) || o.Index == LocalDriving || o.Index == owned)) return null;
            return u.Car;
        }

        // Voiture replacee d'un coup : ce qui est pose dedans la suit (Props), pas ses propres pieces (le transform
        // de la voiture les emmene : deplacees deux fois sinon, attaches tirees d'autant).
        static void MoveCargo(Car c, Vector3 pos, Quaternion rot, Vector3 vel)
        {
            moving = c;
            try { Props.CarMoved(c.Body, pos, rot, vel); }
            finally { moving = null; }
        }

        // Objet replace d'un coup (pose recue) : la voiture sous lui sera cherchee a nouveau.
        public static void Forget(Rigidbody item) { if (item != null) under.Remove(item); }


        static void SetKinematic(Car c, bool on)
        {
            if (c.Body == null || c.Kinematic == on) return;
            c.Kinematic = on;
            if (c.Dt != null)
            {
                if (on) { c.DtWas = c.Dt.enabled; c.Dt.enabled = false; c.Dt.startEngine = false; }
                else
                {
                    // Volant pris alors que le moteur tournait chez l'autre (regime recu a l'instant) : il continue ici.
                    bool hot = c.Index == LocalDriving && c.Rpm > 200f && Time.realtimeSinceStartup - c.LastRemote < 2f;
                    c.Dt.rpm = 0; c.Dt.throttle = 0; c.Dt.enabled = c.DtWas;
                    if (hot) HotStart(c);
                }
            }
            if (c.Axis != null) { if (on) { c.AxisWas = c.Axis.enabled; c.Axis.enabled = false; } else c.Axis.enabled = c.AxisWas; }
            if (c.ModCtrl != null) { if (on) { c.ModCtrlWas = c.ModCtrl.enabled; c.ModCtrl.enabled = false; } else c.ModCtrl.enabled = c.ModCtrlWas; }
            if (c.Sound != null)
            {
                if (on) { c.SoundWasOn = c.Sound.enabled; c.Sound.enabled = true; }
                else c.Sound.enabled = c.SoundWasOn;
            }
            for (int i = 0; i < c.SoundObjs.Length; i++)
            {
                if (c.SoundObjs[i] == null) continue;
                if (on) c.SoundObjsWas[i] = c.SoundObjs[i].activeSelf;
                else c.SoundObjs[i].SetActive(c.SoundObjsWas[i]);
                foreach (PlayMakerFSM f in c.SoundObjs[i].GetComponents<PlayMakerFSM>()) f.enabled = !on;
            }
            if (on) { c.WheelsWas = new bool[c.Wheels.Length]; for (int i = 0; i < c.Wheels.Length; i++) if (c.Wheels[i] != null) { c.WheelsWas[i] = c.Wheels[i].enabled; c.Wheels[i].enabled = false; } }
            else for (int i = 0; i < c.Wheels.Length; i++) if (c.Wheels[i] != null) c.Wheels[i].enabled = c.WheelsWas == null || c.WheelsWas[i];
            if (on)
            {
                c.WasKinematic = c.Body.isKinematic;
                c.Body.isKinematic = true;
                // Copie deplacee au pas de physique (FixedUpdate) : interpolee pour le rendu, elle et ses pieces
                // articulees (banquette, coffre, portieres) -- sans cela, a-coups a 50 Hz chez le passager et
                // pieces qui tremblent en roulant.
                c.InterpWas = new Dictionary<Rigidbody, RigidbodyInterpolation>();
                foreach (Rigidbody rb in c.Body.GetComponentsInChildren<Rigidbody>(true))
                {
                    c.InterpWas[rb] = rb.interpolation;
                    rb.interpolation = RigidbodyInterpolation.Interpolate;
                }
                // (Redevenue copie pendant sa reprise : ses vraies limites de casse sont celles d'avant, pas l'infini du moment.)
                if (c.JointsLater != null) { c.JointsWas = c.JointsLater; c.JointsLater = null; }
                c.SettleUntil = 0f;
                ProtectJoints(c);
            }
            else
            {
                // Reprise en douceur (retour d'un joueur, 08/10 : voiture envolee en prenant le volant) : les pieces
                // articulees (portieres, pieces montees) ont derive sur la copie -- cinematique, deplacee par a-coups,
                // attaches incassables ; redevenue physique, le moteur physique corrigeait tout d'un coup. Pendant 1,5 s :
                // attaches encore incassables, pieces a la vitesse de la voiture, vitesse verticale et rotation bridees a
                // celles recues (+ une marge) ; ensuite les limites de casse d'avant.
                float now0 = Time.realtimeSinceStartup;
                if (c.JointsWas != null) { c.JointsLater = c.JointsWas; c.JointsWas = null; }
                c.Body.isKinematic = c.WasKinematic;
                if (!c.Body.isKinematic)
                {
                    c.Body.velocity = c.Vel; c.Body.angularVelocity = c.AngVel;
                    foreach (Rigidbody rb in c.Body.GetComponentsInChildren<Rigidbody>())
                        if (rb != c.Body && !rb.isKinematic) { rb.velocity = c.Vel; rb.angularVelocity = c.AngVel; }
                    c.SettleUntil = now0 + 1.5f; c.SettleVel = c.Vel; c.SettleAng = c.AngVel;
                }
                else c.SettleUntil = now0;
                if (c.InterpWas != null)
                {
                    foreach (KeyValuePair<Rigidbody, RigidbodyInterpolation> kv in c.InterpWas) if (kv.Key != null) kv.Key.interpolation = kv.Value;
                    c.InterpWas = null;
                }
            }
        }

        // Copie conduite ailleurs : ses attaches cassables (pare-brise, portieres, pieces montees) ne cassent
        // pas ici -- la copie suit par a-coups (recalages) et le pare-brise volait en eclats en roulant. Une
        // vraie casse chez le conducteur est renvoyee par les automates de la voiture (Jobs).
        static void ProtectJoints(Car c)
        {
            if (c.JointsWas == null) c.JointsWas = new Dictionary<Joint, Vector2>();
            foreach (Joint j in c.Body.GetComponentsInChildren<Joint>(true))
            {
                if (j == null || c.JointsWas.ContainsKey(j)) continue;
                if (CarDoors.IsDoorLock(j)) continue;   // verrou de portiere (« Set lock 2 ») : doit pouvoir casser, sinon soudure
                c.JointsWas[j] = new Vector2(j.breakForce, j.breakTorque);
                j.breakForce = Mathf.Infinity;
                j.breakTorque = Mathf.Infinity;
            }
            c.NextJoints = Time.realtimeSinceStartup + 2f;
        }

        // Suivi des copies au pas de physique (MovePosition ne s'applique qu'au pas suivant : appele a chaque
        // image, la voiture avancait par paliers de 20 ms, sans interpolation).
        public static void FixedUpdate()
        {
            if (!scanned) return;
            foreach (Car c in cars)
                if (c.Body != null && c.Kinematic && c.Body.gameObject.activeInHierarchy && !c.Framewise) Follow(c);
                else if (c.Body != null && !c.Kinematic && c.SettleUntil > 0f) Settle(c);
        }

        // Voiture tout juste reprise (voir SetKinematic) : vitesse verticale et rotation bridees ; puis attaches rendues.
        static int settleLogs;
        static void Settle(Car c)
        {
            float now = Time.realtimeSinceStartup;
            if (now >= c.SettleUntil)
            {
                c.SettleUntil = 0f;
                if (c.JointsLater != null)
                {
                    foreach (KeyValuePair<Joint, Vector2> kv in c.JointsLater)
                        if (kv.Key != null) { kv.Key.breakForce = kv.Value.x; kv.Key.breakTorque = kv.Value.y; }
                    c.JointsLater = null;
                }
                return;
            }
            if (c.Body.isKinematic) return;
            Vector3 v = c.Body.velocity, w = c.Body.angularVelocity;
            float vyMax = Mathf.Max(c.SettleVel.y, 0f) + 1.5f, wMax = c.SettleAng.magnitude + 1.5f;
            bool clamped = false;
            if (v.y > vyMax) { v.y = vyMax; c.Body.velocity = v; clamped = true; }
            if (w.magnitude > wMax) { c.Body.angularVelocity = w.normalized * wMax; clamped = true; }
            if (clamped && settleLogs++ < 20) Log.Info("voiture " + c.Key + " reprise : elan bride (montee " + v.y.ToString("F1") + " m/s, rotation " + w.magnitude.ToString("F1") + " rad/s)");
        }

        // Copie ou le joueur local est assis (passager) : deplacee a chaque image par son transform, sans interpolation
        // (ni la sienne ni celle de ses pieces). Au pas de physique avec interpolation, la camera du passager suivait la
        // pose interpolee et les collisionneurs des boutons la pose physique : jusqu'a vitesse x 20 ms d'ecart, variable
        // d'une image a l'autre -- l'icone de la main clignotait, impossible de cliquer en roulant (retour d'un joueur,
        // 08/10). Le transform pose ici met aussi a jour les collisionneurs tout de suite (Unity 5.0).
        static void Ride(Car c, bool on)
        {
            if (on == c.Framewise) return;
            c.Framewise = on;
            if (c.InterpWas == null) return;
            foreach (Rigidbody rb in c.InterpWas.Keys)
                if (rb != null) rb.interpolation = on ? RigidbodyInterpolation.None : RigidbodyInterpolation.Interpolate;
        }

        static void FollowFrame(Car c)
        {
            float dt = Mathf.Min(Time.realtimeSinceStartup - c.LastRemote, 0.3f);
            Vector3 target = c.Pos + c.Vel * dt;
            Quaternion rot = c.Rot;
            if (c.AngVel.sqrMagnitude > 1e-4f)
                rot = Quaternion.AngleAxis(c.AngVel.magnitude * dt * Mathf.Rad2Deg, c.AngVel.normalized) * c.Rot;
            Transform t = c.Body.transform;
            float k = 1f - Mathf.Exp(-15f * Time.deltaTime);
            if ((target - t.position).sqrMagnitude > 25f) { MoveCargo(c, target, rot, c.Vel); t.position = target; t.rotation = rot; return; }
            t.position = Vector3.Lerp(t.position, target, k);
            t.rotation = Quaternion.Slerp(t.rotation, rot, k);
        }

        static void Follow(Car c)
        {
            // Extrapole a partir du dernier etat recu, puis rattrape en douceur.
            float dt = Mathf.Min(Time.realtimeSinceStartup - c.LastRemote, 0.3f);
            Vector3 target = c.Pos + c.Vel * dt;
            Quaternion rot = c.Rot;
            if (c.AngVel.sqrMagnitude > 1e-4f)
                rot = Quaternion.AngleAxis(c.AngVel.magnitude * dt * Mathf.Rad2Deg, c.AngVel.normalized) * c.Rot;
            Transform t = c.Body.transform;
            float k = 1f - Mathf.Exp(-15f * Time.fixedDeltaTime);
            if ((target - t.position).sqrMagnitude > 25f) { MoveCargo(c, target, rot, c.Vel); t.position = target; t.rotation = rot; }
            else
            {
                c.Body.MovePosition(Vector3.Lerp(c.Body.position, target, k));   // pose physique (le transform est interpole pour le rendu)
                c.Body.MoveRotation(Quaternion.Slerp(c.Body.rotation, rot, k));
            }
        }

        // Son du moteur et roues de la copie conduite ailleurs.
        // Braquage du controleur (CarController.steering) : le volant du jeu (SteeringWheel) le lit pour tourner. Sur la copie
        // le controleur est coupe : sans cela le volant restait fige, et les mains du conducteur avec (retour d'un joueur, 08/10).
        static readonly System.Reflection.FieldInfo fSteering = typeof(AxisCarController).BaseType != null ? typeof(AxisCarController).BaseType.GetField("steering", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance) : null;

        static float ModFloat(Component o, System.Reflection.PropertyInfo p) { try { return o != null && p != null ? (float)p.GetValue(o, null) : 0f; } catch { return 0f; } }
        static bool ModBool(Component o, System.Reflection.PropertyInfo p) { try { return o != null && p != null && (bool)p.GetValue(o, null); } catch { return false; } }

        static void Animate(Car c)
        {
            if (c.Dt != null) { c.Dt.rpm = c.Rpm; c.Dt.throttle = c.Throttle; }
            if (c.ModDt != null)
                try
                {
                    if (c.ModRpm != null) c.ModRpm.SetValue(c.ModDt, c.Rpm, null);
                    if (c.ModRun != null) c.ModRun.SetValue(c.ModDt, c.RemoteRunning, null);
                }
                catch { }
            if (c.Axis != null && fSteering != null) try { fSteering.SetValue(c.Axis, Mathf.Clamp(c.Steer, -1f, 1f)); } catch { }
            for (int i = 0; i < c.SoundObjs.Length; i++)
            {
                bool want = (c.SoundMask & (1 << i)) != 0;
                GameObject g = c.SoundObjs[i];
                if (g == null) continue;
                if (g.activeSelf != want) g.SetActive(want);
                if (!want) continue;
                AudioSource a = g.GetComponent<AudioSource>();
                a.pitch = c.SoundPitch[i];
                a.volume = c.SoundVol[i];
                if (!a.isPlaying && a.loop) a.Play();
            }
            StarterFollow(c);
            float fwd = Vector3.Dot(c.Vel, c.Body.transform.forward);
            for (int i = 0; i < c.Wheels.Length; i++)
            {
                Wheel w = c.Wheels[i];
                if (w == null || w.model == null || w.radius <= 0f) continue;
                c.WheelRot[i] += fwd / w.radius * Time.deltaTime;
                float steer = w.maxSteeringAngle * c.Steer;
                w.model.transform.localRotation = Quaternion.Euler(0f, steer, 0f) * Quaternion.AngleAxis(57.29578f * c.WheelRot[i], Vector3.right);
            }
        }

        // Copie : son automate Starter mene a l'etat du moteur du conducteur quand ils divergent depuis 1,5 s (le temps
        // d'un demarrage rejoue normal : contact, demarreur, "Crank up").
        static void StarterFollow(Car c)
        {
            PlayMakerFSM s = c.Starter;
            if (s == null || !s.enabled || !s.gameObject.activeInHierarchy) return;
            StarterLatch(c, s);
            string st = s.ActiveStateName;
            bool on = st == "Running" || st == "Crank up" || st == "Start engine";
            bool off = c.RemoteRunning ? !on : st == "Running";
            float now = Time.realtimeSinceStartup;
            if (!off) { c.StarterOff = 0f; return; }
            if (c.StarterOff <= 0f) { c.StarterOff = now; return; }
            if (now - c.StarterOff < 1.5f) return;
            c.StarterOff = 0f;
            string to = c.RemoteRunning ? (s.Fsm.GetState("Start engine") != null ? "Start engine" : "Running") : (s.Fsm.GetState("Stall engine") != null ? "Stall engine" : null);
            if (to == null) return;
            Replay.Depth++;
            try { Game.SetState(s, to); }
            finally { Replay.Depth--; }
            if (starterLogs++ < 20) Log.Info("moteur " + c.Key + " : " + (c.RemoteRunning ? "en marche" : "coupe") + " chez #" + c.RemoteBy + ", demarreur d'ici " + st + " -> " + to);
        }
        static int starterLogs;

        // Copie : le demarreur tourne (son StarterSound en boucle, "Fuel Mixture") tant que Starter.Starting est vrai -- mis par
        // la cle rejouee ("Motor starting"), remis a faux par son relachement ("Shut off"). Relachement perdu ou arrive dans le
        // desordre : demarreur en boucle chez les autres, jusqu'a vider la batterie de la copie (retour d'un joueur, 10/10 :
        // « des sons qui se jouent en boucle, comme le starter »). Cle plus sur "Motor starting" : Starting remis a faux ;
        // cle restee 10 s sur "Motor starting" : relachee ("Shut off").
        static void StarterLatch(Car c, PlayMakerFSM s)
        {
            FsmBool starting = s.FsmVariables.FindFsmBool("Starting");
            if (starting == null) return;
            if (c.KeyFsm == null && !c.KeyLooked)
            {
                c.KeyLooked = true;
                foreach (PlayMakerFSM f in c.T.GetComponentsInChildren<PlayMakerFSM>(true))
                    if (f.FsmName == "Use" && f.Fsm.GetState("Motor starting") != null && f.Fsm.GetState("Shut off") != null) { c.KeyFsm = f; break; }
            }
            float now = Time.realtimeSinceStartup;
            bool cranking = c.KeyFsm != null && c.KeyFsm.ActiveStateName == "Motor starting";
            if (!cranking) c.KeyCrankSince = 0f;
            else if (c.KeyCrankSince <= 0f) c.KeyCrankSince = now;
            if (cranking && now - c.KeyCrankSince > 10f)
            {
                c.KeyCrankSince = 0f;
                Replay.Depth++;
                try { Game.SetState(c.KeyFsm, "Shut off"); } finally { Replay.Depth--; }
                if (starterLogs++ < 20) Log.Info("moteur " + c.Key + " : cle restee sur le demarreur 10 s (relachement pas recu), relachee ici");
            }
            else if (!cranking && c.KeyFsm != null && starting.Value)
            {
                if (c.LatchSince <= 0f) { c.LatchSince = now; return; }
                if (now - c.LatchSince < 0.5f) return;
                starting.Value = false;
                if (starterLogs++ < 20) Log.Info("moteur " + c.Key + " : demarreur arrete ici (cle " + c.KeyFsm.ActiveStateName + ")");
            }
            c.LatchSince = 0f;
        }

        // Essais : etat du demarreur de la voiture et moteur annonce par celui qui la fait rouler.
        public static string StarterState(string name)
        {
            Car c = Named(name);
            if (c == null) return "?";
            return "demarreur " + (c.Starter != null ? c.Starter.ActiveStateName : "-") + (c.Kinematic ? ", conducteur : " + (c.RemoteRunning ? "en marche" : "coupe") : "");
        }

        static string SoundDiag(Car c)
        {
            SoundController sc = c.Sound;
            if (sc == null) return "pas de SoundController";
            var sb = new System.Text.StringBuilder("sons moteur :");
            foreach (GameObject g in c.SoundObjs)
                if (g != null) { AudioSource a = g.GetComponent<AudioSource>(); sb.Append(' ').Append(g.name).Append(g.activeInHierarchy ? "/on" : "/off").Append('/').Append(a.isPlaying ? "joue" : "-").Append('/').Append(a.pitch.ToString("F2")); }
            if (sb.Length > 0) return sb.ToString();
            sb.Append("SoundController " + (sc.enabled ? "actif" : "coupe") + ", throttle=" + (sc.engineThrottle != null ? sc.engineThrottle.name : "null") + " :");
            foreach (AudioSource a in c.Body.GetComponentsInChildren<AudioSource>(true))
                sb.Append(' ').Append(a.clip != null ? a.clip.name : "-").Append('/').Append(a.isPlaying ? "joue" : "arret").Append('/').Append(a.volume.ToString("F2")).Append(a.enabled && a.gameObject.activeInHierarchy ? "" : "(off)");
            return sb.ToString();
        }

        // Essais : regime et accelerateur envoyes a la place de ceux du moteur local (< 0 : les vrais) ; klaxon annonce.
        static float testRpm = -1f, testThr;
        static bool testHorn;
        public static void TestHorn(bool on) { testHorn = on; }
        static float testSteer = float.NaN;
        public static string TestGear(string name, int gear)
        {
            Car c = Named(name);
            if (c == null || c.Dt == null) return "?";
            System.Reflection.FieldInfo fi = c.Dt.GetType().GetField("gear");
            if (fi == null) return "pas de champ gear";
            fi.SetValue(c.Dt, gear);
            return "rapport " + gear;
        }
        public static string GearState(string name)
        {
            Car c = Named(name);
            if (c == null || c.Body == null) return "?";
            foreach (Transform t in c.Body.GetComponentsInChildren<Transform>(true))
                if (t.name == "Pivot" && t.parent != null && t.parent.name == "Gearstick")
                {
                    if (!wheelRest.ContainsKey(t)) wheelRest[t] = t.localRotation;
                    return "levier " + Quaternion.Angle(wheelRest[t], t.localRotation).ToString("F1") + " deg";
                }
            return "pas de levier";
        }
        public static void TestSteer(float s) { testSteer = s; }
        // Essais : angle du volant (SteeringWheel) de la voiture par rapport a sa pose au premier appel.
        static readonly Dictionary<Transform, Quaternion> wheelRest = new Dictionary<Transform, Quaternion>();
        public static string WheelState(string name)
        {
            Car c = Named(name);
            if (c == null || c.Body == null) return "?";
            foreach (MonoBehaviour m in c.Body.GetComponentsInChildren<MonoBehaviour>(true))
                if (m != null && m.GetType().Name == "SteeringWheel")
                {
                    Transform t = m.transform;
                    if (!wheelRest.ContainsKey(t)) wheelRest[t] = t.localRotation;
                    return "volant " + Quaternion.Angle(wheelRest[t], t.localRotation).ToString("F0") + " deg (braquage recu " + c.Steer.ToString("F2") + ")";
                }
            return "pas de volant";
        }
        public static string HornState(string name)
        {
            Car c = Named(name);
            if (c == null) return "?";
            return c.Horn == null ? "pas de CarHorn" : "klaxon " + (c.Horn.activeInHierarchy ? "allume" : "eteint") + (c.Kinematic ? " (copie, conducteur : " + (c.HornRemote ? "allume" : "eteint") + ")" : "");
        }

        // L'objet appartient-il a une voiture qu'un autre joueur conduit en ce moment ?
        public static bool RemotelyDriven(Transform t)
        {
            Car c = CarOf(t);
            return c != null && Remote(c, Time.realtimeSinceStartup);
        }

        // ... par un joueur assis au volant (pas un moteur laisse tournant, conducteur sorti).
        public static bool RemotelySeated(Transform t)
        {
            Car c = CarOf(t);
            return c != null && c.RemoteDriver >= 0 && Remote(c, Time.realtimeSinceStartup);
        }

        // Volant pris d'une voiture dont le moteur tourne chez un autre (copie : son Drivetrain coupe ici, son
        // automate Starter a l'arret) : sans rien faire, le moteur calait en prenant le volant -- en marche chez
        // l'autre, coupe ici (retour de JD, 06/10 soir). On le relance comme le jeu reprend un moteur qui tourne :
        // contact mis (cle sur "ACC on", Starter.ACC), Drivetrain actif au regime recu ; l'automate Starter, en
        // "ACC", voit plus de 200 tr/min et passe de lui-meme a "Running" (comme un demarrage a la poussette).
        static Car hotCar;
        static float hotUntil, hotRpm, hotAt;

        static void HotStart(Car c)
        {
            hotCar = c;
            hotRpm = Mathf.Max(c.Rpm, 700f);
            hotAt = Time.realtimeSinceStartup;
            hotUntil = hotAt + 4f;
            // Tout de suite (pas a l'image suivante) : regime nul une seule image, et le Starter (souvent deja en
            // "Running" sur la copie) passait a l'arret.
            if (c.Dt != null) { c.Dt.enabled = true; c.Dt.rpm = hotRpm; }
            PlayMakerFSM key = null;
            foreach (PlayMakerFSM f in c.T.GetComponentsInChildren<PlayMakerFSM>(true))
                if (f.FsmName == "Use" && f.Fsm.GetState("ACC on") != null && f.Fsm.GetState("Motor starting") != null) { key = f; break; }
            string ks = key != null ? key.ActiveStateName : "?";
            if (key != null && (ks == "Wait1" || ks == "Wait ACC" || ks == "Motor OFF")) Game.SetState(key, "ACC on");
            Log.Info("moteur repris en marche : " + c.Key + " (regime recu " + c.Rpm.ToString("F0") + ", cle " + ks + ")");
        }

        static void HotTick()
        {
            if (hotCar == null) return;
            Car c = hotCar;
            PlayMakerFSM starter = null;
            if (c.Dt != null && c.T != null)
                foreach (PlayMakerFSM f in c.T.GetComponentsInChildren<PlayMakerFSM>(true))
                    if (f.FsmName == "Starter" && f.Fsm.GetState("Running") != null) { starter = f; break; }
            string st = starter != null ? starter.ActiveStateName : "";
            float now = Time.realtimeSinceStartup;
            // Fini : Starter en marche avec un vrai regime, au moins une seconde apres la prise (sur la copie, il pouvait
            // etre en "Running" a regime nul) ; ou 4 s passees (le moteur fait ensuite ce que fait le jeu).
            bool running = st == "Running" && c.Dt != null && c.Dt.rpm > 200f && now - hotAt > 1f;
            if (c.Dt == null || c.Index != LocalDriving || running || now > hotUntil)
            {
                Log.Info("moteur repris : " + c.Key + " -> " + (st == "Running" ? "en marche" : "demarreur " + (st.Length > 0 ? st : "?")) + ", regime " + (c.Dt != null ? c.Dt.rpm.ToString("F0") : "?"));
                hotCar = null;
                return;
            }
            c.Dt.enabled = true;
            if (c.Dt.rpm < hotRpm) c.Dt.rpm = hotRpm;
        }

        // Essais : allume les sons moteur de la voiture locale (comme le contact quand le moteur tourne).
        public static string TestSounds(string name, bool on)
        {
            Car c = Named(name);
            if (c == null) return "?";
            foreach (GameObject g in c.SoundObjs)
                if (g != null && g.name.StartsWith("Sound"))
                {
                    g.SetActive(on);
                    // Moteur pas vraiment demarre : on fige une hauteur de son pour verifier l'envoi.
                    foreach (PlayMakerFSM f in g.GetComponents<PlayMakerFSM>()) f.enabled = false;
                    g.GetComponent<AudioSource>().pitch = 0.9f;
                }
            return SoundDiag(c);
        }
        public static void TestEngine(float rpm, float thr) { testRpm = rpm; testThr = thr; }

        // Essais : toutes les sources audio de la voiture (clip, joue, volume, hauteur) et son etat.
        public static string AudioState(string name)
        {
            Car c = Named(name);
            if (c == null || c.Body == null) return "?";
            var sb = new System.Text.StringBuilder(name + (c.Kinematic ? " (copie)" : "") + " SoundController " + (c.Sound != null ? (c.Sound.enabled ? "actif" : "coupe") : "-") + ", regime " + (c.Dt != null ? c.Dt.rpm.ToString("F0") : "?") + " :");
            foreach (AudioSource a in c.Body.GetComponentsInChildren<AudioSource>(true))
                if (a.isPlaying && a.volume > 0.001f) sb.Append(' ').Append(a.gameObject.name).Append('/').Append(a.clip != null ? a.clip.name : "-").Append('/').Append(a.volume.ToString("F2")).Append('/').Append(a.pitch.ToString("F2"));
            return sb.ToString();
        }

        // Essais : champs simples du Drivetrain (nombres, booleens) de la voiture.
        public static string DtState(string name)
        {
            Car c = Named(name);
            if (c == null || c.Dt == null) return "?";
            var sb = new System.Text.StringBuilder("drivetrain " + name + " (" + (c.Dt.enabled ? "actif" : "coupe") + ") :");
            foreach (System.Reflection.FieldInfo fi in c.Dt.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
            {
                object v = fi.GetValue(c.Dt);
                if (v is bool || v is int) sb.Append(' ').Append(fi.Name).Append('=').Append(v);
                else if (v is float) sb.Append(' ').Append(fi.Name).Append('=').Append(((float)v).ToString("F1"));
            }
            return sb.ToString();
        }

        // Sortie forcee du vehicule conduit ici (reapparition) : l'etat de sortie du jeu, quelle que soit la vitesse.
        public static bool ExitLocal()
        {
            if (LocalDriving < 0 || LocalDriving >= cars.Count) return false;
            Car c = cars[LocalDriving];
            if (c.Drive == null || c.Drive.Fsm.GetState("Create player") == null) return false;
            Game.SetState(c.Drive, "Create player");
            Log.Info("sortie forcee de " + c.Key);
            return true;
        }

        // Essais : sort le joueur local de la voiture comme la touche ENTREE.
        public static string TestExit(string name)
        {
            Car c = Named(name);
            if (c == null || c.Drive == null) return "?";
            if (c.Drive.ActiveStateName == "Player in car") c.Drive.SendEvent("Key DOWN");
            return c.Drive.ActiveStateName;
        }

        static float SteerOf(Car c)
        {
            float s = 0f;
            foreach (Wheel w in c.Wheels) if (w != null && Mathf.Abs(w.steering) > Mathf.Abs(s)) s = w.steering;
            return s;
        }

        // Moteur en marche ici (regime, ou un son moteur allume par le jeu).
        static bool EngineRunning(Car c)
        {
            if (c.Body == null) return false;
            if (c.Dt != null && c.Dt.enabled && c.Dt.rpm > 150f) return true;
            if (c.ModDt != null && ModBool(c.ModDt, c.ModRun) && ModFloat(c.ModDt, c.ModRpm) > 40f) return true;
            foreach (GameObject g in c.SoundObjs) if (g != null && g.activeSelf) return true;
            return false;
        }

        static void Send(Car c, int mode)
        {
            if (c.Body == null || c.Net < 0) return;   // (absente de la table de l'hote : personne ne la connait sous ce numero)
            bool driven = mode != 0;
            var w = new NetWriter(Msg.Vehicle).U8(Session.LocalId).U8(c.Net).U8(mode)
                .Vec(c.Body.position).Quat(c.Body.rotation).Vec(c.Body.velocity).Vec(c.Body.angularVelocity);
            if (driven)
            {
                float rpm = testRpm >= 0f ? testRpm : c.Dt != null ? c.Dt.rpm : ModFloat(c.ModDt, c.ModRpm);
                float thr = testRpm >= 0f ? testThr : c.Dt != null ? c.Dt.throttle : 0f;
                int mask = 0;
                for (int i = 0; i < c.SoundObjs.Length && i < 14; i++) if (c.SoundObjs[i] != null && c.SoundObjs[i].activeSelf) mask |= 1 << i;   // (14 au plus : 0x4000 klaxon, 0x8000 moteur)
                if ((c.Starter != null && c.Starter.ActiveStateName == "Running") || testRpm > 0f || ModBool(c.ModDt, c.ModRun)) mask |= 0x8000;   // (essais : faux moteur en marche)
                if ((c.Horn != null && c.Horn.activeInHierarchy) || testHorn) mask |= 0x4000;
                w.F32(rpm).F32(thr).F32(float.IsNaN(testSteer) ? SteerOf(c) : testSteer).F32(c.Heat != null ? c.Heat.Value : float.NaN).U16(mask);
                for (int i = 0; i < c.SoundObjs.Length && i < 14; i++)
                    if ((mask & (1 << i)) != 0)
                    {
                        AudioSource a = c.SoundObjs[i].GetComponent<AudioSource>();
                        w.F32(a.pitch).F32(a.volume);
                    }
                // Conducteur : sa camera dans le repere de la voiture, au meme instant que la voiture (la tete de PlayerSync
                // arrive par un autre message : en accelerant, l'ecart d'appariement penchait l'avatar hors du siege,
                // retour d'un joueur, 09/10 : « le conducteur bugue quand la voiture accelere »).
                if (mode == 1) { Transform cam = PlayerSync.LocalCamera; w.Vec(cam != null ? c.Body.transform.InverseTransformPoint(cam.position) : Vector3.zero); }
            }
            Session.SendAll(w, false);
        }

        // Etat de la simulation (SimVars) : (empreinte de la cle, valeur). Les commandes (tirette de starter) seulement
        // quand on rend la voiture : en roulant, Jobs rejoue la tirette (un passager qui la tire ne doit pas etre
        // ramene en arriere chaque seconde par la valeur du conducteur).
        static void SendSim(Car c, int flags, bool reliable)
        {
            if (c.Body == null || c.Net < 0 || c.Sims.Length == 0 || !Session.Active || Session.RemoteCount == 0) return;
            bool controls = (flags & SimFinal) != 0;
            int n = 0;
            foreach (Sim s in c.Sims) if ((controls || !s.Control) && n < 100) n++;
            var w = new NetWriter(Msg.Vehicle).U8(Session.LocalId).U8(c.Net).U8(M_SIM).U8(flags).U8(n);
            int k = 0;
            foreach (Sim s in c.Sims)
            {
                if (!controls && s.Control) continue;
                if (k++ >= n) break;
                w.I32((int)s.Hash).F32(s.Var.Value);
            }
            Session.SendAll(w, reliable);
            if ((flags & SimFinal) != 0) Log.Info("voiture " + c.Key + " rendue : etat du moteur envoye (" + SimSummary(c) + ")");
        }

        static void OnSim(Car c, int who, NetReader r)
        {
            int flags = r.U8(), n = r.U8();
            float now = Time.realtimeSinceStartup;
            // On fait autorite (conduite ici, moteur tournant a nous) ; ou instantane de l'hote d'une voiture qu'un
            // autre fait rouler ici, ou qu'on vient de rendre (notre derniere valeur est en route).
            bool skip = c == null || c.Index == LocalDriving || c.Index == owned
                        || ((flags & SimParked) != 0 && (Remote(c, now) || now < c.AuthorityUntil));
            for (int i = 0; i < n; i++)
            {
                uint h = (uint)r.I32();
                float v = r.F32();
                if (skip) continue;
                foreach (Sim s in c.Sims) if (s.Hash == h) { s.Var.Value = v; break; }
            }
            if (skip) return;
            if ((flags & SimFinal) != 0) Log.Info("voiture " + c.Key + " rendue par #" + who + " : etat du moteur repris (" + SimSummary(c) + ")");
            else if (!c.SimLogged && (flags & SimParked) == 0) { c.SimLogged = true; Log.Info("voiture " + c.Key + " : etat du moteur de #" + who + " suivi (" + SimSummary(c) + ")"); }
        }

        // Batterie, temperature du moteur, tirette de starter, amorcage : pour les journaux.
        static string SimSummary(Car c)
        {
            if (c == null || c.Sims.Length == 0) return "pas de simulation suivie";
            var sb = new System.Text.StringBuilder();
            foreach (Sim s in c.Sims)
            {
                string k = s.Key.Substring(s.Key.LastIndexOf(':') + 1);
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(k).Append('=').Append(s.Var.Value.ToString("F2"));
            }
            return sb.ToString();
        }

        // Avatar d'un joueur qui conduit chez lui : position et rotation dans la copie locale de la
        // voiture (calculees par rapport a l'etat de la voiture envoye en meme temps : pas de tremblement).
        // (joueur local au volant -- son avatar a la troisieme personne : la voiture conduite ici)
        static Car LocalCar(int player) { return player == Session.LocalId && LocalDriving >= 0 && LocalDriving < cars.Count && cars[LocalDriving].Body != null ? cars[LocalDriving] : null; }

        public static Transform RemoteCarTransform(int player)
        {
            Car lc = LocalCar(player); if (lc != null) return lc.Body.transform;
            foreach (Car c in cars) if (c.RemoteDriver == player && c.Body != null) return c.Body.transform;
            return null;
        }

        // Nom de la voiture que 'player' conduit chez lui (null : aucune).
        public static string RemoteCarName(int player)
        {
            Car lc = LocalCar(player); if (lc != null) return lc.Name;
            foreach (Car c in cars) if (c.RemoteDriver == player) return c.Name;
            return null;
        }

        // Vitesse de la voiture que ce joueur conduit (0 : il ne conduit pas ici).
        public static Vector3 RemoteVelocity(int player)
        {
            Car lc = LocalCar(player); if (lc != null) return lc.Body.velocity;
            foreach (Car c in cars) if (c.RemoteDriver == player && c.Body != null) return c.Vel;
            return Vector3.zero;
        }

        public static float RemoteSpeed(int player)
        {
            Car lc = LocalCar(player); if (lc != null) return lc.Body.velocity.magnitude;
            foreach (Car c in cars) if (c.RemoteDriver == player && c.Body != null) return c.Vel.magnitude;
            return 0f;
        }

        // Camera du conducteur 'player' dans le repere de sa voiture (envoyee avec elle) ; faux si inconnue.
        public static bool RemoteHeadLocal(int player, out Vector3 head)
        {
            Car lc = LocalCar(player);
            if (lc != null && PlayerSync.LocalCamera != null) { head = lc.Body.transform.InverseTransformPoint(ThirdPerson.EyePosition); return true; }
            foreach (Car c in cars) if (c.RemoteDriver == player && c.RemoteHead != Vector3.zero) { head = c.RemoteHead; return true; }
            head = Vector3.zero;
            return false;
        }

        public static bool SeatPose(int player, Vector3 feet, out Vector3 pos, out Quaternion rot)
        {
            Car lc = LocalCar(player);
            if (lc != null) { pos = feet; rot = lc.Body.transform.rotation; return true; }
            foreach (Car c in cars)
            {
                if (c.RemoteDriver != player || c.Body == null) continue;
                Vector3 local = Quaternion.Inverse(c.Rot) * (feet - c.Pos);
                pos = c.Body.transform.TransformPoint(local);
                rot = c.Body.transform.rotation;
                return true;
            }
            pos = feet; rot = Quaternion.identity;
            return false;
        }

        // Numero inconnu ici (voiture de l'hote pas encore active ici) : note une fois, nouveau releve (10 s au plus souvent).
        static void Missing(int idx, bool rescan)
        {
            float now = Time.realtimeSinceStartup;
            if (missingLogged.Add(idx)) Log.Info("voitures : numero " + idx + " (" + (idx < netKeys.Count && netKeys[idx] != null ? netKeys[idx] : "?") + ") absente ici");
            if (rescan && tableSeen && now - lastMissingScan >= 10f) { lastMissingScan = now; nextScan = Mathf.Min(nextScan, now + 0.5f); }
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            int idx = r.U8();
            int mode = r.U8();
            if (idx == NoCar)
            {
                if (mode == M_TABLE) OnTable(r);
                else if (mode == M_KEYS) OnKeys(from, r);
                return;
            }
            if (mode == M_SIM)
            {
                if (Session.IsHost)
                {
                    byte[] rest = r.Rest();
                    bool rel = rest.Length > 0 && (rest[0] & SimFinal) != 0;
                    Session.Broadcast(new NetWriter(Msg.Vehicle).U8(who).U8(idx).U8(M_SIM).Raw(rest), rel, who);
                }
                if (!scanned) return;
                Car sc = ByNet(idx);
                if (sc == null) { Missing(idx, (r.U8() & SimParked) == 0); return; }
                OnSim(sc, who, r);
                return;
            }
            bool driven = mode != 0;
            Vector3 pos = r.Vec();
            Quaternion rot = r.Quat();
            Vector3 vel = r.Vec(), ang = r.Vec();
            float rpm = 0f, thr = 0f, steer = 0f, heat = float.NaN;
            int smask = 0;
            var spitch = new List<float>();
            if (driven)
            {
                rpm = r.F32(); thr = r.F32(); steer = r.F32(); heat = r.F32(); smask = r.U16();
                for (int i = 0; i < 14; i++) if ((smask & (1 << i)) != 0) { spitch.Add(r.F32()); spitch.Add(r.F32()); }
            }
            Vector3 dhead = mode == 1 && r.More ? r.Vec() : Vector3.zero;
            if (Session.IsHost)
            {
                var fw = new NetWriter(Msg.Vehicle).U8(who).U8(idx).U8(mode).Vec(pos).Quat(rot).Vec(vel).Vec(ang);
                if (driven) { fw.F32(rpm).F32(thr).F32(steer).F32(heat).U16(smask); foreach (float v in spitch) fw.F32(v); if (mode == 1) fw.Vec(dhead); }
                Session.Broadcast(fw, false, who);
            }
            if (!scanned) return;
            Car c = ByNet(idx);
            if (c == null || c.Body == null) { if (driven) Missing(idx, true); return; }
            if (c.Index == LocalDriving) return;              // je la conduis : je garde la main
            if (c.Index == owned && mode != 1) return;        // mon moteur tourne : je garde la main
            if (c.Index == owned) { owned = -1; Log.Info(c.Key + " prise par #" + who); }
            if (driven)
            {
                c.RemoteBy = who;
                c.RemoteDriver = mode == 1 ? who : -1;
                c.RemoteHead = dhead;
                c.LastRemote = Time.realtimeSinceStartup;
                c.Pos = pos; c.Rot = rot; c.Vel = vel; c.AngVel = ang;
                c.Rpm = rpm; c.Throttle = thr; c.Steer = steer; c.SoundMask = smask & 0x3FFF; c.RemoteHeat = heat;
                c.HornRemote = (smask & 0x4000) != 0;
                c.RemoteRunning = (smask & 0x8000) != 0;
                for (int i = 0, k = 0; i < c.SoundObjs.Length && i < 16; i++)
                    if ((smask & (1 << i)) != 0 && k + 1 < spitch.Count) { c.SoundPitch[i] = spitch[k]; c.SoundVol[i] = spitch[k + 1]; k += 2; }
                return;
            }
            // Sans conducteur : recalage si la voiture locale a derive (> 1 m ou > 10 degres).
            if (c.RemoteBy == who) { c.RemoteBy = -1; c.RemoteDriver = -1; }
            c.Vel = vel; c.AngVel = ang;
            Transform t = c.Body.transform;
            if ((t.position - pos).sqrMagnitude > 1f || Quaternion.Angle(t.rotation, rot) > 10f)
            {
                SetKinematic(c, false);
                MoveCargo(c, pos, rot, vel);   // ce qui est pose dedans suit (avant que la voiture bouge)
                t.position = pos;
                t.rotation = rot;
                c.Body.velocity = vel;
                c.Body.angularVelocity = ang;
                Log.Info("voiture " + c.Key + " recalee sur " + (who == 0 ? "l'hote" : "#" + who));
            }
        }

        // Essais : 1) amene le joueur a la portiere (le jeu active les objets proches),
        // 2) le met au volant comme l'automate du jeu (etat 'Check seat').
        public static string TestEnter(string name, bool seat)
        {
            Car c = Named(name);
            if (c == null || c.Drive == null) return null;
            if (!seat)
            {
                GameObject p = GameObject.Find("PLAYER");
                var cc = p.GetComponent<CharacterController>();
                cc.enabled = false;
                p.transform.position = c.Drive.transform.position + Vector3.up * 0.5f;
                cc.enabled = true;
                return "a cote, automate " + (c.Drive.gameObject.activeInHierarchy ? "actif" : "inactif");
            }
            // 'Press return' attend la touche d'entree (GetButtonDown -> Key DOWN).
            if (c.Drive.ActiveStateName == "Press return") c.Drive.SendEvent("Key DOWN");
            else Game.SetState(c.Drive, "Check seat");
            return c.Drive.ActiveStateName;
        }

        // Essais : moteur d'un vehicule de mod (Poro) : regime, en marche ; 'start' : le met en marche.
        public static string ModEngine(string name, bool start)
        {
            Car c = Named(name);
            if (c == null || c.ModDt == null) return "pas de moteur de mod";
            if (start && c.ModRun != null) try { c.ModRun.SetValue(c.ModDt, true, null); } catch { }
            return "regime " + ModFloat(c.ModDt, c.ModRpm).ToString("F0") + (ModBool(c.ModDt, c.ModRun) ? " en marche" : " arrete") + (c.ModCtrl != null ? (c.ModCtrl.enabled ? ", controleur actif" : ", controleur coupe") : "");
        }

        public static Rigidbody Body(string name)
        {
            Car c = Named(name);
            return c != null ? c.Body : null;
        }

        // ------------------------------------------------------------ essais automatiques (appeles par Autotest)
        // taxi : [Test] TaxiActiver=1 (defaut) active JOBS/TAXIJOB/MACHTWAGEN des deux cotes a 8 s s'il dort, puis
        // releve a 9 s. Toutes les 5 s, chacun note la voiture (cle, rang, numero reseau, pose, copie ou non,
        // automate de conduite) et ce que les autres modules en suivent. Hote : monte a 20 s, au volant a 27 s,
        // poussee a 8 m/s de 30 a 42 s puis de 58 a 64 s (l'invite doit la voir suivre). Invite : ouvre la
        // portiere arriere droite a 44 s, la lache, la referme a 48 s ; s'assoit a l'arriere a 52 s (passager
        // emmene de 58 a 64 s), se leve a 72 s. Options (invite) : TaxiRecale=1 (taxi decale de 2 m a 21 s, recale par
        // l'hote : portiere a sa place dans le repere du taxi a 25 s) ; TaxiRanger=1 (taxi range ici a 66 s, le joueur
        // assis : decroche a 67 s, taxi remis a 70 s).
        // reprise : [Test] TestVoiture (SORBET). Hote : au volant a 22 s, moteur (sons, regime d'essai) a 24 s,
        // valeurs reconnaissables posees a 27 s (batterie 87.25, temperatures 61.5, tirette 0.4) ; moteur coupe a
        // 33 s et sortie a 35 s (voiture rendue : etat envoye, fiable) -- [Test] RepriseMoteur=1 : sort moteur
        // tournant (il en garde la main ; l'invite la prend en montant : batterie et temperatures par le flux d'une
        // seconde, la tirette reste a Jobs). Invite : monte a 45 s, au volant a 50 s, sort a 65 s (moteur arrete :
        // etat rendu). Les deux notent l'etat du moteur toutes les 5 s de 20 a 80 s : l'invite doit montrer les
        // valeurs de l'hote des 28 s (copie) et apres 35 s, puis l'hote celles de l'invite apres 65 s.
        static int tStep, tRec;
        static float tLog;
        static Vector3 tDoor;
        static Rigidbody tDoorBody;

        // Essais : corps de la piece 'name' de la voiture (portiere...).
        static Rigidbody Part(Car c, string name)
        {
            foreach (Rigidbody rb in c.Body.GetComponentsInChildren<Rigidbody>(true)) if (rb.name == name) return rb;
            return null;
        }

        public static void Test(string mode, float t)
        {
            if (mode != "taxi" && mode != "reprise") return;
            bool host = Session.IsHost;
            string name = mode == "taxi" ? "MACHTWAGEN" : Config.Get("Test", "TestVoiture", "SORBET(190-200psi)");
            float now = Time.realtimeSinceStartup;
            if (mode == "taxi")
            {
                if (t > 8f && tStep == 0)
                {
                    tStep = 1;
                    GameObject taxi = Game.FindAny("JOBS/TAXIJOB/MACHTWAGEN");
                    if (taxi == null) Log.Info("autotest : taxi JOBS/TAXIJOB/MACHTWAGEN introuvable");
                    else if (!taxi.activeSelf && Config.GetInt("Test", "TaxiActiver", 1) != 0) { taxi.SetActive(true); Log.Info("autotest : taxi active"); }
                    else Log.Info("autotest : taxi " + (taxi.activeInHierarchy ? "deja actif" : "inactif (TaxiActiver=0)"));
                }
                if (t > 9f && tStep == 1) { tStep = 2; if (scanned) Scan(); Log.Info("autotest : taxi releve, " + TaxiState(name)); }
                if (host)
                {
                    if (t > 20f && tStep == 2) { tStep = 3; Log.Info("autotest : taxi " + TestEnter(name, false)); }
                    if (t > 27f && tStep == 3) { tStep = 4; Log.Info("autotest : taxi volant -> " + TestEnter(name, true)); }
                    Rigidbody b = Body(name);
                    if (b != null && tStep >= 4 && ((t > 30f && t < 42f) || (t > 58f && t < 64f)))
                    { Vector3 f = b.transform.forward; f.y = 0; b.velocity = f.normalized * 8f + Vector3.up * Mathf.Min(b.velocity.y, 0f); }
                }
                else
                {
                    // [Test] TaxiRecale=1 : taxi decale de 2 m ici a 21 s (gare des deux cotes, portieres deja suivies par
                    // Props) ; le recalage de l'hote (2 s) le ramene : la portiere doit rester a sa place dans le repere du
                    // taxi (pas deplacee deux fois).
                    if (t > 21f && tRec == 0 && Config.GetInt("Test", "TaxiRecale", 0) != 0)
                    {
                        tRec = 1;
                        Car tc = Named(name);
                        tDoorBody = tc != null && tc.Body != null ? Part(tc, "DoorRear(right)") : null;
                        if (tDoorBody == null) Log.Info("autotest : taxi recale : pas de portiere DoorRear(right)");
                        else
                        {
                            tDoor = tc.T.InverseTransformPoint(tDoorBody.position);
                            tc.T.position += tc.T.right * 2f;
                            Log.Info("autotest : taxi decale de 2 m ici, portiere en " + tDoor.ToString("F2") + " (repere du taxi)");
                        }
                    }
                    if (t > 25f && tRec == 1)
                    {
                        tRec = 2;
                        Car tc = Named(name);
                        if (tc != null && tDoorBody != null)
                        {
                            float d = (tc.T.InverseTransformPoint(tDoorBody.position) - tDoor).magnitude;
                            Log.Info("autotest : taxi recale " + (d < 0.2f ? "OK" : "ECHEC") + " : portiere a " + d.ToString("F2") + " m de sa place dans le repere du taxi (attendu < 0,2), taxi " + TaxiState(name));
                        }
                    }
                    bool ranger = Config.GetInt("Test", "TaxiRanger", 0) != 0;
                    if (t > 44f && tStep == 2) { tStep = 3; Log.Info("autotest : taxi " + CarDoors.TestOpen(name, true, "DoorRear(right)")); }
                    if (t > 44.6f && tStep == 3) { tStep = 4; Log.Info("autotest : taxi lache " + CarDoors.TestState(name, "Mouse off", "DoorRear(right)")); }
                    if (t > 48f && tStep == 4) { tStep = 5; Log.Info("autotest : taxi referme " + CarDoors.TestGrab(name, "DoorRear(right)")); }
                    if (t > 52f && tStep == 5) { tStep = 6; Log.Info("autotest : taxi " + Seats.TestSit(name, 1)); }
                    if (!ranger && t > 72f && tStep == 6) { tStep = 7; Log.Info("autotest : taxi " + Seats.TestLeave()); }
                    // [Test] TaxiRanger=1 : taxi range ici (SetActive(false), comme a la fin du service) avec le joueur assis a
                    // l'arriere : il doit etre decroche (PLAYER actif) ; taxi remis a 70 s, le joueur pose a 3 m a cote.
                    if (ranger && t > 66f && tStep == 6)
                    {
                        tStep = 10;
                        GameObject taxi = Game.FindAny("JOBS/TAXIJOB/MACHTWAGEN");
                        if (taxi != null) taxi.SetActive(false);
                        Log.Info("autotest : taxi range ici (assis : " + Seats.Seated + ")");
                    }
                    if (t > 67f && tStep == 10)
                    {
                        tStep = 11;
                        GameObject pl = GameObject.Find("PLAYER");   // (introuvable si inactif)
                        Log.Info("autotest : taxi range " + (!Seats.Seated && pl != null ? "OK" : "ECHEC") + " : passager " + (Seats.Seated ? "toujours assis" : "sorti")
                                 + ", PLAYER " + (pl != null ? "actif en " + pl.transform.position.ToString("F1") : "inactif"));
                    }
                    if (t > 70f && tStep == 11)
                    {
                        tStep = 12;
                        GameObject taxi = Game.FindAny("JOBS/TAXIJOB/MACHTWAGEN");
                        GameObject pl = GameObject.Find("PLAYER");
                        if (taxi != null && pl != null)
                        {
                            var cc = pl.GetComponent<CharacterController>();
                            if (cc != null) cc.enabled = false;
                            pl.transform.position = taxi.transform.position + taxi.transform.right * 3f + Vector3.up * 0.5f;
                            if (cc != null) cc.enabled = true;
                        }
                        if (taxi != null) taxi.SetActive(true);
                        Log.Info("autotest : taxi remis ici");
                    }
                }
                if (t > 10f && t < 90f && now >= tLog) { tLog = now + 5f; Log.Info("autotest : taxi (" + (host ? "hote" : "invite") + ") " + TaxiState(name) + " | " + CarDoors.StateOf(name, "DoorRear(right)")); }
                return;
            }
            // reprise
            if (host)
            {
                if (t > 15f && tStep == 0) { tStep = 1; Log.Info("autotest : reprise " + TestEnter(name, false)); }
                if (t > 22f && tStep == 1) { tStep = 2; Log.Info("autotest : reprise volant -> " + TestEnter(name, true)); }
                if (t > 24f && tStep == 2) { tStep = 3; TestEngine(2000f, 0.3f); Log.Info("autotest : reprise moteur " + TestSounds(name, true)); }
                if (t > 27f && tStep == 3) { tStep = 4; Log.Info("autotest : reprise valeurs posees : " + TestSetSim(name)); }
                if (t > 33f && tStep == 4)
                {
                    tStep = 5;
                    if (Config.GetInt("Test", "RepriseMoteur", 0) == 0) { TestEngine(-1f, 0f); Log.Info("autotest : reprise moteur coupe " + TestSounds(name, false)); }
                }
                if (t > 35f && tStep == 5) { tStep = 6; Log.Info("autotest : reprise sortie -> " + TestExit(name)); }
                if (t > 70f && tStep == 6) { tStep = 7; TestEngine(-1f, 0f); }
            }
            else
            {
                if (t > 45f && tStep == 0) { tStep = 1; Log.Info("autotest : reprise " + TestEnter(name, false)); }
                if (t > 50f && tStep == 1) { tStep = 2; Log.Info("autotest : reprise volant -> " + TestEnter(name, true)); }
                if (t > 65f && tStep == 2) { tStep = 3; Log.Info("autotest : reprise sortie -> " + TestExit(name)); }
            }
            if (t > 20f && t < 80f && now >= tLog) { tLog = now + 5f; Log.Info("autotest : reprise (" + (host ? "hote" : "invite") + ") " + TaxiState(name)); }
        }

        // Essais : ce qu'on sait de la voiture 'name' ici.
        static string TaxiState(string name)
        {
            Car c = Named(name);
            if (c == null) return name + " inconnue ici (" + cars.Count + " voitures, table " + netKeys.Count + (tableSeen || Session.IsHost ? "" : ", pas encore recue") + ")";
            float now = Time.realtimeSinceStartup;
            string role = c.Index == LocalDriving ? "conduite ici" : c.Index == owned ? "moteur tournant a nous" : Remote(c, now) ? (c.RemoteDriver >= 0 ? "copie, conduite par #" + c.RemoteDriver : "copie, moteur tournant chez #" + c.RemoteBy) : "garee";
            return c.Key + " rang " + c.Index + " numero " + c.Net + (c.Body == null ? " (corps detruit)" : (c.Body.gameObject.activeInHierarchy ? "" : " (inactive)") + " en " + c.Body.position.ToString("F1") + " rot " + c.Body.rotation.eulerAngles.y.ToString("F0"))
                   + ", " + role + ", conduite " + (c.Drive != null ? c.Drive.ActiveStateName : "?") + ", regime " + (c.Dt != null ? c.Dt.rpm.ToString("F0") : "?")
                   + ", PlayerCurrentVehicle '" + (curVehicle != null ? curVehicle.Value : "?") + "'"
                   + " | suivi : portieres " + CarDoors.CountFor(c.Key) + ", tableau de bord " + CarVisuals.CountFor(c.Key) + ", places " + Seats.CountFor(c.Key)
                   + " | moteur : " + SimSummary(c);
        }

        // Essais : valeurs reconnaissables dans la simulation de 'name' (batterie, temperatures, tirette).
        static string TestSetSim(string name)
        {
            Car c = Named(name);
            if (c == null) return "?";
            foreach (Sim s in c.Sims)
            {
                string v = s.Key.Substring(s.Key.LastIndexOf('.') + 1);
                if (v == "Charge") s.Var.Value = 87.25f;
                else if (s.Key.Contains(":Cooling.")) s.Var.Value = 61.5f;
                else if (s.Key.Contains(":OperatingTemp.")) s.Var.Value = 0.62f;
                else if (s.Key.Contains(":Use.Choke")) s.Var.Value = 0.4f;
            }
            return SimSummary(c);
        }
    }
}
