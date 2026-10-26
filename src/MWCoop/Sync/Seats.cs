using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Places passagers (le jeu n'a qu'une place, celle du conducteur), faites comme la sienne :
    //  - places : d'apres les yeux du conducteur (DriverHeadPivot + 27 cm vers l'avant, mesure au volant
    //    de la SORBET) : avant droite en symetrique ; banquette a l'arriere pour SORBET, CORRIS, BACHGLOTZ
    //    (demande de JD, 08/10 : une voiture, banquette a 85 cm comme les autres, devant l'essieu arriere) et le
    //    taxi (MACHTWAGEN, sous JOBS/TAXIJOB : voitures de VehicleSync, par leur cle) ; GIFU : le passager et trois
    //    places sur la couchette de la cabine (08/10) ;
    //  - on entre dans l'habitacle jusqu'au siege (pieds sur le plancher, a moins de 38 cm de cote et
    //    50 cm en long de la place) : l'icone passager du jeu s'affiche (GUIpassenger, comme le volant) ;
    //  - ENTREE : le joueur est accroche a la voiture LA OU IL EST (pas de teleportation), tourne vers
    //    l'avant ; l'automate Crouch du joueur passe en « Incar » (variable PlayerInCar), exactement comme
    //    pour le conducteur : il abaisse la camera a la hauteur assise et fige les deplacements ;
    //  - ENTREE de nouveau : decroche sur place (dans l'habitacle), Crouch « Get out » releve la camera.
    // Jamais quand le jeu s'apprete a faire conduire (zone du conducteur en attente d'ENTREE). Les autres
    // voient l'avatar assis, la tete a la place reelle de sa camera (envoyee), qui suit son regard. Les
    // commandes du vehicule restent accessibles et sont rejouees chez tous (Jobs).
    //  - Ceinture du passager avant (pour le decor, demande de JD, 08/10 : le jeu n'en a que pour le conducteur) :
    //    on vise la boucle (celle du conducteur, en miroir) et on clique, comme le conducteur. Bouclee : une copie
    //    en miroir de la ceinture bouclee du conducteur (Fastened_mesh, ou Seatbelts/Close), la ceinture du
    //    passager rangee (PassengerBelt, ou Seatbelts/Passenger) cachee ; chez tous (envoyee avec la place).
    //  - Ceinture du CONDUCTEUR (retour de JD, 07/10 : les autres ne la voyaient pas) : le jeu ne montre la ceinture bouclee
    //    (Fastened_mesh) qu'a celui qui la porte (PlayerSeatbeltsOn, a chaque joueur). Ses automates (Seatbelts/...) ne
    //    sont plus rejoues par Jobs (le rejeu bouclait la ceinture... du joueur d'en face, et un clic du passager sur sa
    //    boucle detachait celle du conducteur) : le conducteur envoie son etat (Msg.Seat, place 0xFF), les autres voient
    //    une copie de la ceinture bouclee sur son avatar (un peu en avant, comme le passager) et la sangle pendante
    //    (DriverBelt/HandleUpPivot) cachee.
    public static partial class Seats   // (partie : SeatFiles.cs, places de l'outil de JD)
    {
        class Seat { public string Car; public Transform CarT; public int Index; public Vector3 Head; public float ZoneX = 0.38f, ZoneZ = 0.5f, MaxX = 9f; public bool Bed; public float Yaw; public bool Stand; }   // (Stand : place debout, appuye -- cabine du tracteur)   // (Bed : ridelle d'une remorque, on y monte depuis le sol ; Yaw : sens de la place, degres, 0 = l'avant)
        class Remote { public string Car; public int Index; public Vector3 Head; public bool Belt; }
        // Ceinture du passager avant d'une voiture : modeles du jeu (ceinture bouclee du conducteur, ceinture du
        // passager rangee), boucle (repere voiture), copie en miroir montree quand le passager est attache.
        class Belt { public GameObject Fastened, Open, Copy; public Vector3 Buckle, DriverBuckle, Top, Floor; public bool Anchors; public bool OpenHidden; public GameObject DriverOpen, DriverCopy; public bool DriverOpenHidden; public Mesh Bulged; public bool CopyPlain; public Vector3 MeshBuckle; public Dictionary<int, GameObject> Rear = new Dictionary<int, GameObject>(); public Dictionary<int, bool> RearPlain = new Dictionary<int, bool>(); }
        static readonly Dictionary<int, bool> driverBelt = new Dictionary<int, bool>();   // conducteurs distants : ceinture bouclee
        static bool driverBeltSent;
        static float driverBeltResend;
        const int DriverBeltIndex = 0xFF;
        static readonly Dictionary<Transform, Belt> belts = new Dictionary<Transform, Belt>();
        static bool beltOn, beltHint;
        const float BeltForward = 0f;   // (sangle reliee a ses attaches, quitte a traverser le torse de l'avatar : demande de JD, 09/10 ; avant 0,17 m en avant, elle flottait)
        static readonly List<PlayMakerFSM> driveTriggers = new List<PlayMakerFSM>();
        // Zone du conducteur de chaque voiture (toutes, une place comprise) et voiture qui la porte.
        static readonly List<KeyValuePair<PlayMakerFSM, Transform>> driverZones = new List<KeyValuePair<PlayMakerFSM, Transform>>();
        static readonly HashSet<PlayMakerFSM> zonesOff = new HashSet<PlayMakerFSM>();
        static float nextZones;
        static bool iconOn;

        static readonly List<Seat> seats = new List<Seat>();
        static readonly Dictionary<int, Remote> remote = new Dictionary<int, Remote>();
        static Seat current;
        static Transform pivot, player, cam;
        static CharacterController controller;
        static PlayMakerFSM crouch;
        static float nextScan = -1, satAt, nextResend;
        static int gen = -1;   // VehicleSync.Generation au dernier releve
        static Vector3 headLocal;
        static int debugFrames;
        static float crouchCheckAt = -1;

        public static bool Seated { get { return current != null; } }
        public static Transform LocalCar { get { return current != null ? current.CarT : null; } }
        public static string SeatedCarKey { get { return current != null ? current.Car : null; } }   // (cle VehicleSync de la voiture)
        // Voiture ou le joueur local est assis en passager (null : aucune).
        public static Transform SeatedCar { get { return current != null ? current.CarT : null; } }
        public static float LocalSeatX { get { return current != null ? current.Head.x : 0f; } }          // (repere de la voiture, droite +)
        public static Vector3 LocalSeatPos { get { return current != null ? current.Head : Vector3.zero; } }

        public static void OnLevelLoaded()
        {
            zonesOff.Clear(); driverZones.Clear();
            seats.Clear(); current = null; pivot = null; player = cam = null; controller = null; crouch = null; remote.Clear(); gen = -1;
            SeatFiles.OnLevelLoaded();
            belts.Clear(); rearModels.Clear(); beltOn = beltHint = false; driverBelt.Clear(); driverBeltSent = false; blockedHandle = null;
            nextScan = PlayerSync.InGame ? Time.realtimeSinceStartup + 14f : -1;
        }

        static void Scan()
        {
            // (Assis : la place gardee pointe sur l'ancienne liste, retrouvee ci-dessous par voiture et rang.)
            Seat was = current;
            seats.Clear();
            driveTriggers.Clear();
            driverZones.Clear();
            gen = VehicleSync.Generation;
            for (int ci = 0; ci < VehicleSync.LocalCount; ci++)
            {
                Rigidbody rb = VehicleSync.LocalBody(ci);
                if (rb == null) continue;
                string n = VehicleSync.LocalKey(ci);
                foreach (PlayMakerFSM f in rb.GetComponentsInChildren<PlayMakerFSM>(true))
                    if (f.FsmName == "PlayerTrigger" && f.gameObject.name.StartsWith("DriveTrigger")) driverZones.Add(new KeyValuePair<PlayMakerFSM, Transform>(f, rb.transform));
                if (n.StartsWith("JONNEZ"))
                {
                    // Jonnez : une place derriere le conducteur, au bout de la selle (demande de JD, 10/10) ; rang 1 (pas de
                    // ceinture). Zone serree : la moto est etroite.
                    Transform jh = Find(rb.transform, "DriverHeadPivot");
                    if (jh != null)
                    {
                        Vector3 jd = rb.transform.InverseTransformPoint(jh.position) + EyeFromPivot;
                        seats.Add(new Seat { Car = n, CarT = rb.transform, Index = 1, Head = new Vector3(jd.x, jd.y + Config.GetFloat("Test", "JonnezLeve", 0.04f), jd.z - Config.GetFloat("Test", "JonnezRecul", 0.38f)), ZoneX = 0.45f, ZoneZ = 0.45f, MaxX = 0.6f });
                    }
                    continue;
                }
                if (n.StartsWith("FLATBED"))
                {
                    // Remorque (Menor, 14/10 : « s'asseoir dans la benne » ; JD, 21/10 : des places sur toute remorque, assis sur les
                    // ridelles, tourne vers l'interieur, jambes pendantes -- son dessin) : quatre places sur le haut des ridelles, deux
                    // par cote, sans ceinture. Ridelles : le plus gros maillage de la remorque (bornes dans son repere) ; yeux a 0,72 m
                    // au-dessus du bord. On y monte depuis le sol a cote ou depuis le plateau (Bed : pieds jusqu'a 2,3 m sous les yeux).
                    // [Test] RemorquePlaces=x,y,z : retrait du bord (vers l'interieur), hauteur des yeux, ecart avant/arriere.
                    MeshFilter body = null; float bestVol = 0f;
                    foreach (MeshFilter mf in rb.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (mf.sharedMesh == null || mf.name.ToLowerInvariant().Contains("wheel") || mf.name.ToLowerInvariant().Contains("tire")) continue;
                        Vector3 bs = Vector3.Scale(mf.sharedMesh.bounds.size, mf.transform.lossyScale);
                        float vol = bs.x * bs.y * bs.z;
                        if (vol > bestVol) { bestVol = vol; body = mf; }
                    }
                    if (body != null)
                    {
                        Bounds mb = body.sharedMesh.bounds;
                        Vector3 lo = new Vector3(1e9f, 1e9f, 1e9f), hi = -lo;
                        for (int cj = 0; cj < 8; cj++)
                        {
                            Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((cj & 1) != 0 ? 1f : -1f, (cj & 2) != 0 ? 1f : -1f, (cj & 4) != 0 ? 1f : -1f));
                            Vector3 l = rb.transform.InverseTransformPoint(body.transform.TransformPoint(corner));
                            lo = Vector3.Min(lo, l); hi = Vector3.Max(hi, l);
                        }
                        Vector3 k = ParseV(Config.Get("Test", "RemorquePlaces", ""), new Vector3(0.22f, 0.66f, 0.9f));
                        float top = hi.y, halfW = (hi.x - lo.x) * 0.5f - k.x, zc = (lo.z + hi.z) * 0.5f;
                        for (int i = 0; i < 4; i++)
                        {
                            float sx = (i % 2 == 0 ? -1f : 1f) * halfW;
                            seats.Add(new Seat { Car = n, CarT = rb.transform, Index = 1 + i, Head = new Vector3(sx, top + k.y, zc + (i < 2 ? k.z : -k.z)),
                                                 ZoneX = 1.4f, ZoneZ = 1.0f, MaxX = halfW + 1.4f, Bed = true, Yaw = sx < 0f ? 90f : -90f });   // (face a l'interieur)
                        }
                        if (seatLogs++ < 4) Log.Info("places passagers : ridelles de " + n + " (" + body.name + ") : bord a " + top.ToString("F2") + " m, largeur " + (hi.x - lo.x).ToString("F2") + ", 4 places");
                    }
                    continue;
                }
                if (n == "BOAT" && Game.Msc)
                {
                    // Barque de My Summer Car (joueurs du 20/10 : « pas de places pour les passagers ») : le pilote a l'arriere, pres du
                    // moteur ; la place en miroir le mettait sur ses genoux. Deux places sur le banc du milieu, une a l'avant, sans ceinture.
                    Transform bh = Find(rb.transform, "DriverHeadPivot");
                    if (bh != null)
                    {
                        Vector3 bd = rb.transform.InverseTransformPoint(bh.position) + EyeFromPivot;
                        float bz = Config.GetFloat("Test", "BarqueRecul", 1.1f), bx = Config.GetFloat("Test", "BarqueEcart", 0.3f);
                        seats.Add(new Seat { Car = n, CarT = rb.transform, Index = 1, Head = new Vector3(bx, bd.y, bd.z - bz), ZoneX = 0.45f, ZoneZ = 0.5f, MaxX = 0.6f });
                        seats.Add(new Seat { Car = n, CarT = rb.transform, Index = 2, Head = new Vector3(-bx, bd.y, bd.z - bz), ZoneX = 0.45f, ZoneZ = 0.5f, MaxX = 0.6f });
                        seats.Add(new Seat { Car = n, CarT = rb.transform, Index = 3, Head = new Vector3(0f, bd.y, bd.z - 2f * bz), ZoneX = 0.6f, ZoneZ = 0.5f, MaxX = 0.6f });
                    }
                    continue;
                }
                foreach (PlayMakerFSM f in rb.GetComponentsInChildren<PlayMakerFSM>(true))
                    if (f.FsmName == "PlayerTrigger" && f.gameObject.name.StartsWith("DriveTrigger")) driveTriggers.Add(f);
                Transform dhp = Find(rb.transform, "DriverHeadPivot");
                if (dhp == null)
                {
                    // RAM 1500 (mod) : pas de DriverHeadPivot ; banquette avant d'une piece -- passager a droite (en miroir du
                    // conducteur) et une place au milieu (rang 1, sans ceinture).
                    Vector3 rh;
                    if (n == "Ram1500" && ModDriverHead(rb.transform, out rh))
                    {
                        seats.Add(new Seat { Car = n, CarT = rb.transform, Index = 0, Head = new Vector3(-rh.x, rh.y, rh.z) });
                        seats.Add(new Seat { Car = n, CarT = rb.transform, Index = 1, Head = new Vector3(0.03f, rh.y, rh.z), ZoneX = 0.3f, MaxX = 0.35f });
                    }
                    continue;
                }
                Vector3 d = rb.transform.InverseTransformPoint(dhp.position) + EyeFromPivot;
                if (n.StartsWith("KEKMET"))
                {
                    // Tracteur (Dr.Robi, 19/10 : « trois joueurs dans les coins arriere de la cabine, appuyes a la carrosserie » ; dessin de
                    // JD, 22/10 : debout, la tete a la vitre arriere) : pas de siege passager ; trois places DEBOUT derriere le conducteur --
                    // coin arriere gauche, coin arriere droit, cote droit --, on y monte depuis le sol a cote (Bed). La cabine est basse
                    // (1 m sous le toit) : debout a demi plie, les yeux a ceux du conducteur (pose accroupie de l'avatar). [Test] TracteurPlaces=
                    // x,y,z (ecart, hauteur des yeux par rapport a ceux du conducteur, recul), TracteurCote=x,y,z (la place de droite).
                    Vector3 tk = ParseV(Config.Get("Test", "TracteurPlaces", ""), new Vector3(0.28f, -0.12f, 0.30f));
                    Vector3 tc = ParseV(Config.Get("Test", "TracteurCote", ""), new Vector3(0.45f, -0.12f, -0.05f));
                    seats.Add(new Seat { Car = n, CarT = rb.transform, Index = 1, Head = new Vector3(-tk.x, d.y + tk.y, d.z - tk.z), ZoneX = 0.8f, ZoneZ = 0.8f, MaxX = 1.3f, Bed = true, Stand = true });
                    seats.Add(new Seat { Car = n, CarT = rb.transform, Index = 2, Head = new Vector3(tk.x, d.y + tk.y, d.z - tk.z), ZoneX = 0.8f, ZoneZ = 0.8f, MaxX = 1.3f, Bed = true, Stand = true });
                    seats.Add(new Seat { Car = n, CarT = rb.transform, Index = 3, Head = new Vector3(tc.x, d.y + tc.y, d.z + tc.z), ZoneX = 0.8f, ZoneZ = 0.8f, MaxX = 1.3f, Bed = true, Stand = true });
                    if (seatLogs++ < 4) Log.Info("places passagers : tracteur " + n + " : 3 places derriere le conducteur (yeux " + d.ToString("F2") + ")");
                    continue;
                }
                // (My Summer Car : les yeux sont 7 cm vers le milieu par rapport au pivot, et l'avatar a son corps 8 cm a droite de ses yeux
                // (pose de conduite) : en miroir simple, le passager avant s'asseyait 15 cm vers le milieu de son siege -- essai SATSUMA, 21/10)
                float xm = Game.Msc ? 2f * EyeFromPivotMsc.x : 0f;
                seats.Add(new Seat { Car = n, CarT = rb.transform, Index = 0, Head = new Vector3(-d.x + xm, d.y, d.z), ZoneZ = Vaz.IsVaz(rb.transform) ? 0.85f : 0.5f });   // (VAZ-2108 : baquets plus en arriere, on n'approche que l'espace des pieds depuis la portiere -- zone plus longue)
                // (taxi et ses copies -- mods Machtwagen300D, SECONDMACHTWAGEN : noms en minuscules ou prefixes, sans banquette
                // avant, retour de Menos 10/10 : « on ne peut pas s'asseoir a l'arriere »)
                // (My Summer Car : SATSUMA, FERNDALE et RCO_RUSCKO12 ont une banquette ; HAYOSIKO, la place avant seulement)
                // (pack rallye de JD, 20/10 : la Lancer Evo III, copie de la SORBET a 4 portes, a aussi sa banquette)
                if (n.StartsWith("SORBET") || n.StartsWith("CORRIS") || n.StartsWith("BACHGLOTZ") || n.ToUpperInvariant().Contains("MACHTWAGEN") || n == "MITSUBISHI LANCER EVO III"
                    || Game.Msc && (n.StartsWith("SATSUMA") || n.StartsWith("FERNDALE") || n.StartsWith("RCO_RUSCKO")))
                {
                    // Banquette : 85 cm derriere, un peu plus haute. SORBET : le toit descend a l'arriere, la tete du passager le
                    // traversait (JD, 21/10) : 8 cm sous les yeux du conducteur ([Test] BanquetteSorbet, m).
                    // (et 17 cm plus en avant : a 85 cm le buste rentrait dans la custode, le bras sortait par la tole -- capture du 21/10 ;
                    // [Test] BanquetteSorbetRecul, m)
                    // (CORRIS, la Rivett coupe : toit bas, 3 cm seulement entre les yeux du passager arriere et le pavillon a +6 cm -- la
                    // tete sortait du toit, capture du 24/10 ; [Test] BanquetteCorris, m)
                    float by = n.StartsWith("SORBET") ? Config.GetFloat("Test", "BanquetteSorbet", -0.08f) : n.StartsWith("CORRIS") ? Config.GetFloat("Test", "BanquetteCorris", -0.10f) : 0.06f;
                    float bz = n.StartsWith("SORBET") ? Config.GetFloat("Test", "BanquetteSorbetRecul", 0.68f) : 0.85f;
                    seats.Add(new Seat { Car = n, CarT = rb.transform, Index = 1, Head = new Vector3(d.x, d.y + by, d.z - bz) });
                    seats.Add(new Seat { Car = n, CarT = rb.transform, Index = 2, Head = new Vector3(-d.x + xm, d.y + by, d.z - bz) });
                }
                if (n.StartsWith("GIFU"))
                {
                    // Couchette de la cabine (derriere les sieges) : trois places assises cote a cote (demande d'un joueur, 08/10).
                    float bz = d.z + GifuBed.z, by = d.y + GifuBed.y;
                    // (On ne tient pas debout derriere les sieges : zone large, depuis les portieres ; la place avant, plus
                    // proche, passe d'abord tant qu'elle est libre.)
                    for (int k = 0; k < 3; k++) seats.Add(new Seat { Car = n, CarT = rb.transform, Index = 1 + k, Head = new Vector3((k - 1) * GifuBed.x, by, bz), ZoneX = 1.1f, ZoneZ = 1.1f, MaxX = 1.05f });   // (parois de la cabine a +-1,0 m)
                }
            }
            FileSeats();   // (places/<jeu>/<voiture>.txt de l'outil des places : remplacent celles calculees ci-dessus)
            if (was != null)
                foreach (Seat x in seats) if (x.Car == was.Car && x.Index == was.Index) { current = x; break; }
            Log.Info("places passagers : " + seats.Count);
        }

        static int seatLogs;
        // Couchette de la GIFU par rapport aux yeux du conducteur : ecart entre les places (x), hauteur (y), recul (z). Coupe de
        // la cabine (essai sondeplaces) : couchette a 1,75 m (repere du camion), toit 2,60, paroi arriere z 1,9, yeux du
        // conducteur 2,05 / z 2,99. Dessous du toit a 2,42 : 67 cm seulement au-dessus de la couchette -> yeux juste sous le toit
        // (2,28), dos a la paroi ; assis tasse (le corps s'enfonce un peu dans la couchette).
        static Vector3 GifuBed { get { return ParseV(Config.Get("Test", "GifuCouchette", ""), new Vector3(0.5f, 0.23f, -0.80f)); } }
        static Vector3 ParseV(string s, Vector3 def)
        {
            string[] c = s.Split(',');
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float x, y, z;
            if (c.Length == 3 && float.TryParse(c[0], System.Globalization.NumberStyles.Float, ci, out x) && float.TryParse(c[1], System.Globalization.NumberStyles.Float, ci, out y) && float.TryParse(c[2], System.Globalization.NumberStyles.Float, ci, out z)) return new Vector3(x, y, z);
            return def;
        }

        // Yeux du conducteur par rapport a DriverHeadPivot (repere voiture), mesures au volant de la SORBET.
        // (My Summer Car : mesure au volant de la HAYOSIKO, 21/10 -- camera a 10 cm devant le pivot seulement, 7 cm vers le
        // milieu ; avec les 27 cm de la SORBET le conducteur distant etait trop en avant, capture d'un joueur dans la SATSUMA)
        static readonly Vector3 EyeFromPivotMwc = new Vector3(0f, -0.03f, 0.27f), EyeFromPivotMsc = new Vector3(0.07f, 0.03f, 0.10f);
        public static Vector3 EyeFromPivot { get { return Game.Msc ? EyeFromPivotMsc : EyeFromPivotMwc; } }

        // Yeux du conducteur au repos (repere de la voiture 'car'), d'apres son DriverHeadPivot ; faux sans pivot.
        static readonly Dictionary<Transform, Transform> headPivots = new Dictionary<Transform, Transform>();
        public static bool DriverHead(Transform car, out Vector3 local)
        {
            local = Vector3.zero;
            if (car == null) return false;
            Transform dhp;
            if (!headPivots.TryGetValue(car, out dhp) || (dhp == null && !ReferenceEquals(dhp, null))) { dhp = Find(car, "DriverHeadPivot"); headPivots[car] = dhp; }
            if (dhp == null) return ModDriverHead(car, out local);
            local = car.InverseTransformPoint(dhp.position) + EyeFromPivot;
            return true;
        }

        // Yeux du conducteur d'une voiture de mod sans DriverHeadPivot : RAM 1500, PlayerTrigger/DriveTrigger (le pivot ou le mod
        // range le joueur) + 0,6 m (mesure au volant, 13/10 : yeux a (-0,39 ; 1,68 ; 0,38), pivot a (-0,39 ; 1,04 ; 0,44)).
        static bool ModDriverHead(Transform car, out Vector3 local)
        {
            local = Vector3.zero;
            if (car == null || car.name != "Ram1500") return false;
            Transform tr = car.Find("PlayerTrigger/DriveTrigger");
            if (tr == null) return false;
            local = car.InverseTransformPoint(tr.position) + new Vector3(0f, 0.6f, -0.06f);
            return true;
        }

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { Transform r = Find(c, name); if (r != null) return r; }
            return null;
        }

        static bool FindPlayer()
        {
            if (player != null && cam != null) return true;
            GameObject go = Game.PlayerGo;
            if (go == null) return false;
            player = go.transform;
            controller = go.GetComponent<CharacterController>();
            cam = PlayerSync.LocalCamera ?? player;
            crouch = Game.FsmOn(go, "Crouch");
            return true;
        }

        public static void Update()
        {
            if (!Session.Active || nextScan < 0 || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            // Liste des voitures changee apres le premier releve (taxi active) : releve tout de suite.
            if (now >= nextScan || (gen >= 0 && (gen != VehicleSync.Generation || FilesChanged))) { nextScan = now + 30f; Scan(); }
            if (!FindPlayer()) return;
            DriverBeltSend(now);
            DriverBeltHeal(now);
            if (now >= nextZones) { nextZones = now + 0.2f; DriverZones(); }
            if (current != null) TrackSpeed(current.CarT);
            if (current != null)
            {
                // Voiture detruite, ou rangee (taxi remis en place par son travail : SetActive(false), rejoue chez
                // tous) : le joueur accroche dessous serait inactif, sans camera ni commandes -- decroche tout de suite.
                if (pivot == null || current.CarT == null || !current.CarT.gameObject.activeInHierarchy)
                {
                    if (current.CarT != null) Log.Info("passager : " + current.Car + " rangee (inactive : " + Recon.Path(current.CarT) + ")");
                    Leave();
                    return;
                }
                // Endormi a sa place (couchette de la GIFU, retour d'un joueur, 10/10 : « l'avatar reste debout, flotte au
                // reveil, puis on ne peut plus s'asseoir, le jeu croit qu'on l'est ») : le jeu couche le joueur lui-meme ;
                // la place est rendue sans toucher au joueur (ni position, ni commandes : le jeu les reprend au reveil).
                if (Game.GlobalBool("PlayerSleeps")) { LeaveForSleep(); return; }
                if (debugFrames > 0)
                {
                    debugFrames--;
                    Log.Info("passager : image " + Time.frameCount + " joueur " + current.CarT.InverseTransformPoint(player.position).ToString("F3") + " local " + player.localPosition.ToString("F3")
                             + " parent " + (player.parent != null ? player.parent.name : "-") + " echelle " + player.localScale.ToString("F2") + " cc " + (controller != null && controller.enabled)
                             + " pivot " + current.CarT.InverseTransformPoint(pivot.position).ToString("F3"));
                }
                if (Input.GetKeyDown(KeyCode.Return) && now - satAt > 0.6f)
                {
                    // Pas de sortie en roulant (demande de JD, 05/10) : on descend a l'arret.
                    if (Moving(current.CarT)) { Refuse(Lang.T("La voiture roule : attends qu'elle s'arr\u00EAte pour descendre", "The car is moving: wait until it stops to get out")); return; }
                    Leave(); return;
                }
                // Tete : la ou la camera s'est posee (le jeu l'abaisse en 0,4 s), puis renvoyee de temps en temps.
                if (now >= nextResend && now - satAt > 0.7f)
                {
                    nextResend = now + 5f;
                    headLocal = current.CarT.InverseTransformPoint(cam.position);
                    SendSeat(current.Car, current.Index, headLocal);
                }
                BeltInput();
                UpdateBelts();
                return;
            }
            // Sorti dans l'habitacle : une fois la camera relevee par le jeu, accroupi si le toit est au-dessus.
            if (crouchCheckAt > 0 && now >= crouchCheckAt)
            {
                crouchCheckAt = -1;
                if (crouch != null && crouch.ActiveStateName == "Wait key" && UnderRoof()) Game.SetState(crouch, "Move down 1");
            }
            UpdateBelts();
            Seat best = null;
            if (VehicleSync.LocalDriving < 0 && !Game.GlobalBool("PlayerSeated") && !InDriverZone()) best = InZone();
            if (best != null) TrackSpeed(best.CarT);
            Icon(best != null);
            if (best != null && Input.GetKeyDown(KeyCode.Return))
            {
                if (Moving(best.CarT)) Refuse(Lang.T("La voiture roule : on monte \u00E0 l'arr\u00EAt", "The car is moving: get in once it stops"));
                else Sit(best);
            }
        }

        // Vitesse de la voiture d'apres sa position (la copie d'une voiture conduite par un autre est cinematique : sa
        // vitesse physique reste nulle). Plus de 1,5 m/s (5 km/h) : elle roule.
        static Transform speedCar;
        static Vector3 speedPos;
        static float speedAt, speedVal, refusedAt;

        static bool Moving(Transform car)
        {
            if (car == null) return false;
            float now = Time.realtimeSinceStartup;
            if (car != speedCar || now - speedAt > 0.5f) { speedCar = car; speedPos = car.position; speedAt = now; speedVal = 0f; }
            Rigidbody rb = car.GetComponent<Rigidbody>();
            float v = rb != null && !rb.isKinematic ? rb.velocity.magnitude : speedVal;
            return v > 1.5f;
        }

        // Chaque image : vitesse mesuree de la voiture ou l'on est assis, ou de la plus proche place libre.
        static void TrackSpeed(Transform car)
        {
            if (car == null) return;
            float now = Time.realtimeSinceStartup;
            if (car != speedCar) { speedCar = car; speedPos = car.position; speedAt = now; speedVal = 0f; return; }
            float dt = now - speedAt;
            if (dt < 0.1f) return;
            float v = (car.position - speedPos).magnitude / dt;
            speedVal = dt > 0.5f ? v : Mathf.Lerp(speedVal, v, 0.5f);
            speedPos = car.position; speedAt = now;
        }

        static void Refuse(string text)
        {
            if (Time.realtimeSinceStartup - refusedAt < 2f) return;
            refusedAt = Time.realtimeSinceStartup;
            Hud.Toast(text);
        }

        // La place ou se tient le joueur : dans l'habitacle (pieds sur le plancher, pas dehors), a moins de
        // 38 cm de cote et 50 cm en long du siege ; la plus proche.
        static Seat InZone()
        {
            Seat best = null;
            float bestD = 10f;   // (zones bornees par place ; couchette de la GIFU : jusqu'a 1,1 m)
            foreach (Seat s in seats)
            {
                if (s.CarT == null || SeatTaken(s)) continue;
                if ((s.CarT.position - player.position).sqrMagnitude > 25f || !s.CarT.gameObject.activeInHierarchy) continue;   // (taxi range : inactif)
                Vector3 p = s.CarT.InverseTransformPoint(player.position);
                if (!InSeatZone(s, p)) continue;
                float dx = Mathf.Abs(p.x - s.Head.x), dz = Mathf.Abs(p.z - (s.Head.z - 0.1f));
                float d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        // p : ancre du joueur (25 cm au-dessus de ses pieds) dans le repere de la voiture. Pieds a moins de 1,6 m sous les yeux de
        // la place assise : sinon, debout par terre a cote de la cabine haute de la GIFU, on s'asseyait dehors, sous la cabine
        // (retour de JD, 08/10 ; sol a -0,15, ancre a +0,10 : passait le seul seuil de -0,3). Couchette : dans la cabine (MaxX).
        static bool InSeatZone(Seat s, Vector3 p)
        {
            float low = s.Bed ? s.Head.y - 2.3f : Mathf.Max(-0.3f, s.Head.y - 1.6f);   // (plateau : depuis le sol a cote de la remorque)
            if (p.y < low || p.y > s.Head.y || Mathf.Abs(p.x) > s.MaxX) return false;
            return Mathf.Abs(p.x - s.Head.x) <= s.ZoneX && Mathf.Abs(p.z - (s.Head.z - 0.1f)) <= s.ZoneZ;
        }

        // Zone du conducteur coupee pour le joueur d'ici (son automate 'PlayerTrigger' arrete, icone du volant eteinte) :
        //  - voiture conduite par un autre joueur, assis au volant : le jeu proposait quand meme la place (retour de
        //    JD, 06/10). Un moteur laisse tournant, conducteur sorti, ne coupe rien : on reprend le volant (06/10 soir) ;
        //  - voiture ou l'on est assis en passager : la zone deborde sur la place avant, et l'icone du volant passait
        //    par-dessus les commandes du tableau de bord.
        // Rendue (automate rallume, en attente du joueur) des que ce n'est plus le cas. Jamais celle ou l'on conduit.
        static void DriverZones()
        {
            foreach (KeyValuePair<PlayMakerFSM, Transform> z in driverZones)
            {
                PlayMakerFSM f = z.Key;
                if (f == null || z.Value == null) continue;
                bool off = f.ActiveStateName != "Player in car"
                           && ((current != null && current.CarT == z.Value) || VehicleSync.RemotelySeated(z.Value));
                if (off && !zonesOff.Contains(f))
                {
                    if (f.ActiveStateName == "Press return") Game.SetGlobalBool("GUIdrive", false);
                    f.enabled = false;
                    zonesOff.Add(f);
                    Log.Info("passager : zone du conducteur de " + z.Value.name + " coupee ici (" + (current != null && current.CarT == z.Value ? "assis en passager" : "conduite par un autre") + ")");
                }
                else if (!off && zonesOff.Contains(f))
                {
                    zonesOff.Remove(f);
                    f.enabled = true;
                    if (f.Fsm.GetState("Wait for player") != null) Game.SetState(f, "Wait for player");
                    Log.Info("passager : zone du conducteur de " + z.Value.name + " rendue");
                }
            }
            // (une icone du volant laissee allumee par une zone coupee en cours de route)
            if (zonesOff.Count > 0 && VehicleSync.LocalDriving < 0 && Game.GlobalBool("GUIdrive"))
            {
                bool any = false;
                foreach (KeyValuePair<PlayMakerFSM, Transform> z in driverZones)
                    if (z.Key != null && z.Key.enabled && z.Key.ActiveStateName == "Press return") any = true;
                if (!any) Game.SetGlobalBool("GUIdrive", false);
            }
        }

        // Le jeu s'apprete a faire conduire le joueur (zone du conducteur, attente d'ENTREE) ?
        static bool InDriverZone()
        {
            foreach (PlayMakerFSM f in driveTriggers)
                if (f != null && (f.ActiveStateName == "Press return" || f.ActiveStateName == "Player in car")) return true;
            return false;
        }

        // Icone passager du jeu (comme le volant pour le conducteur) ; on ne l'eteint que si on l'a allumee.
        static void Icon(bool on)
        {
            if (on) { Game.SetGlobalBool("GUIpassenger", true); iconOn = true; }
            else if (iconOn) { Game.SetGlobalBool("GUIpassenger", false); iconOn = false; }
        }

        static bool SeatTaken(Seat s)
        {
            foreach (Remote r in remote.Values) if (r.Car == s.Car && r.Index == s.Index) return true;
            return false;
        }

        // Comme le conducteur : l'automate Crouch du joueur passe en « Incar » (camera assise, joueur fige).
        static void InCar(bool on)
        {
            FsmBool b = crouch != null ? crouch.FsmVariables.FindFsmBool("PlayerInCar") : null;
            if (b != null) b.Value = on;
            else Log.Warn("passager : variable PlayerInCar introuvable (automate Crouch)");
        }

        static void Sit(Seat s)
        {
            Icon(false);
            pivot = new GameObject("MWCoop-SiegePassager").transform;
            pivot.parent = s.CarT;
            pivot.position = player.position;          // la ou il est : pas de teleportation
            pivot.localRotation = Quaternion.Euler(0f, s.Yaw, 0f);   // (sens de la place : l'avant, ou l'interieur pour une ridelle)
            // Comme l'automate Stopping du jeu (PlayerStop), mais tout de suite : moteur de deplacement et
            // commandes coupes AVANT de bouger le joueur -- sinon le CharacterMotor, une image encore actif
            // avec le controleur coupe, se croit en l'air et remonte le joueur de sa hauteur de marche (0,4 m).
            Motor(false);
            if (controller != null) controller.enabled = false;
            Game.SetGlobalBool("PlayerStop", true);
            player.parent = pivot;
            player.localPosition = Vector3.zero;
            player.localRotation = Quaternion.identity; // tourne vers l'avant de la voiture
            InCar(true);
            current = s;
            satAt = Time.realtimeSinceStartup;
            nextResend = satAt + 0.7f;
            debugFrames = Config.GetInt("Test", "JournalPassager", 0);
            headLocal = s.Head;
            Log.Info("passager : assis dans " + s.Car + " (place " + s.Index + ") en " + s.CarT.InverseTransformPoint(player.position).ToString("F2"));
            SendSeat(s.Car, s.Index, s.Head);
        }

        static void LeaveForSleep()
        {
            string car = current != null ? current.Car : "?";
            current = null;
            beltOn = false;
            BlockHandle(false);
            BeltHint(null);
            Drop(null);
            if (player != null && pivot != null && player.parent == pivot) player.parent = null;
            InCar(false);
            if (pivot != null) Object.Destroy(pivot.gameObject);
            pivot = null;
            Log.Info("passager : endormi dans " + car + ", place rendue (le jeu couche le joueur)");
            SendSeat("", -1, Vector3.zero);
        }

        static void Leave()
        {
            current = null;
            beltOn = false;
            BlockHandle(false);
            BeltHint(null);
            Drop(null);
            if (player != null)
            {
                player.parent = null;                   // sur place, dans l'habitacle
                player.rotation = Quaternion.Euler(0f, player.eulerAngles.y, 0f);
            }
            InCar(false);
            if (controller != null) controller.enabled = true;
            if (player != null) Motor(true);
            Game.SetGlobalBool("PlayerStop", false);
            if (pivot != null) Object.Destroy(pivot.gameObject);
            pivot = null;
            crouchCheckAt = Time.realtimeSinceStartup + 0.6f;
            Log.Info("passager : sorti");
            SendSeat("", -1, Vector3.zero);
        }

        static bool UnderRoof()
        {
            foreach (RaycastHit h in Physics.RaycastAll(player.position, Vector3.up, 1.5f))
                if (h.collider != null && !h.collider.isTrigger && h.collider.transform.root != player.root && VehicleSync.CarRoot(h.collider.transform) != null) return true;
            return false;
        }

        static void Motor(bool on)
        {
            foreach (string n in new[] { "CharacterMotor", "FPSInputController" })
            {
                var b = player.GetComponent(n) as Behaviour;
                if (b != null) b.enabled = on;
            }
        }

        static void SendSeat(string car, int index, Vector3 head)
        {
            if (!Session.Active) return;
            Session.SendAll(new NetWriter(Msg.Seat).U8(Session.LocalId).Str(car).U8(index + 1).Vec(head).U8(beltOn ? 1 : 0), true);   // (places arriere comprises)
        }

        // ---- ceintures
        // Automate d'une ceinture du conducteur (sous un objet 'Seatbelts') : reserve ici, jamais rejoue.
        public static bool IsBeltFsm(PlayMakerFSM f)
        {
            for (Transform t = f.transform; t != null; t = t.parent)
                if (t.name == "Seatbelts") { Replay.Claim(f, "ceinture"); return true; }
            return false;
        }

        // Conducteur ici : ceinture bouclee (variable du jeu) envoyee a chaque changement, et toutes les 5 s bouclee.
        static void DriverBeltSend(float now)
        {
            bool on = VehicleSync.LocalDriving >= 0 && Game.GlobalBool("PlayerSeatbeltsOn");
            if (on == driverBeltSent && (!on || now < driverBeltResend)) return;
            if (on != driverBeltSent) Log.Info("ceinture conducteur : " + (on ? "bouclee" : "detachee") + " (" + VehicleSync.LocalDrivingName + ")");
            driverBeltSent = on;
            driverBeltResend = now + 5f;
            Session.SendAll(new NetWriter(Msg.Seat).U8(Session.LocalId).Str(VehicleSync.LocalDrivingName ?? "").U8(DriverBeltIndex).U8(on ? 1 : 0), true);
        }
        // Ceinture du conducteur qu'on ne peut plus detacher (retour de Mikol, 11/10 : « parfois, dans toutes les voitures ») :
        // bouclee (PlayerSeatbeltsOn, Locked de la languette du conducteur), mais la boucle (SeatbeltLock, automate 'Use' du
        // jeu) eteinte, ou eteinte puis rallumee -- un automate rallume repart de son debut, "Wait player" : « FASTEN SEATBELT »,
        // et le clic ne fait plus rien (on ne sort pas d'une voiture ceinture bouclee). Remise en « UNFASTEN SEATBELT »
        // (Wait player 2) au bout d'une seconde.
        static Transform healCar;
        static PlayMakerFSM healLock, healHandle;
        static float healSince = -1f;
        static readonly HashSet<string> LockFastened = new HashSet<string> { "Wait player 2", "Wait button 2", "State 2", "State 3" };
        static void DriverBeltHeal(float now)
        {
            Transform car = VehicleSync.LocalDrivingRoot;
            if (car == null || !Game.GlobalBool("PlayerSeatbeltsOn")) { healSince = -1f; return; }
            if (car != healCar)
            {
                healCar = car;
                Transform lk = FindUnder(car, "SeatbeltLock", null), up = FindUnder(car, "HandleUpPivot", "DriverBelt") ?? FindUnder(car, "HandleUpPivot", "Seatbelts");   // (CORRIS, GIFU : sous Seatbelts)
                Transform hd = up != null ? FindUnder(up, "SeatbeltHandle", null) : null;
                healLock = lk != null ? Game.FsmOn(lk.gameObject, "Use") : null;
                healHandle = hd != null ? Game.FsmOn(hd.gameObject, "Use") : null;
            }
            if (healLock == null || healHandle == null) return;
            // (Locked : ceinture ordinaire bouclee -- pas le harnais de la CORRIS, ni les vehicules sans ceinture)
            HutongGames.PlayMaker.FsmBool locked = healHandle.FsmVariables.GetFsmBool("Locked");
            bool broken = locked != null && locked.Value && (!healLock.gameObject.activeInHierarchy || !healLock.enabled || !LockFastened.Contains(healLock.ActiveStateName));
            if (!broken) { healSince = -1f; return; }
            if (healSince < 0f) { healSince = now; return; }
            if (now - healSince < 1f) return;
            healSince = -1f;
            string was = !healLock.gameObject.activeInHierarchy ? "eteinte" : !healLock.enabled ? "automate coupe" : healLock.ActiveStateName;
            if (!healLock.gameObject.activeSelf) healLock.gameObject.SetActive(true);
            if (!healLock.enabled) healLock.enabled = true;
            if (!healLock.gameObject.activeInHierarchy) return;
            Game.SetState(healLock, "Wait player 2");
            Log.Info("ceinture conducteur : boucle de " + car.name + " remise a detacher (elle etait " + was + ")");
        }

        // Essais : boucle du conducteur eteinte puis rallumee (son automate repart en « FASTEN SEATBELT »).
        public static string TestBreakLock(string car)
        {
            Rigidbody b = VehicleSync.Body(car);
            Transform lk = b != null ? FindUnder(b.transform, "SeatbeltLock", null) : null;
            if (lk == null) return "pas de SeatbeltLock";
            lk.gameObject.SetActive(false); lk.gameObject.SetActive(true);
            PlayMakerFSM lu = Game.FsmOn(lk.gameObject, "Use");
            return "boucle eteinte puis rallumee : " + (lu != null ? lu.ActiveStateName : "?");
        }

        static Transform FindUnder(Transform t, string name, string parent)
        {
            if (t.name == name && (parent == null || (t.parent != null && t.parent.name == parent))) return t;
            foreach (Transform c in t) { Transform r = FindUnder(c, name, parent); if (r != null) return r; }
            return null;
        }
        static Transform MirrorBelt(Transform driverUp)
        {
            try
            {
                Transform sb = driverUp.parent;
                var pb = new GameObject("PassengerBelt");
                pb.transform.SetParent(sb, false);
                pb.transform.localScale = new Vector3(-1f, 1f, 1f);   // (miroir gauche-droite dans le repere de Seatbelts, a l'origine de la voiture)
                var clone = (GameObject)Object.Instantiate(driverUp.gameObject);
                clone.name = "HandleUpPivot";
                clone.transform.SetParent(pb.transform, false);
                clone.transform.localPosition = driverUp.localPosition; clone.transform.localRotation = driverUp.localRotation; clone.transform.localScale = driverUp.localScale;
                foreach (PlayMakerFSM fz in clone.GetComponentsInChildren<PlayMakerFSM>(true)) Object.Destroy(fz);
                foreach (Collider cz in clone.GetComponentsInChildren<Collider>(true)) Object.Destroy(cz);
                clone.SetActive(true);
                Log.Info("ceinture passager : copie en miroir de celle du conducteur ajoutee a " + sb.root.name);
                return pb.transform;
            }
            catch (System.Exception e) { Log.Warn("ceinture passager (miroir) : " + e.Message); return null; }
        }

        // Ceinture du passager local bouclee (accident : le passager attache survit au choc qui a tue un conducteur non attache).
        public static bool LocalBelted { get { return current != null && beltOn; } }

        // (essai Autotest=mscceintures : ce que BeltOf retient pour 'car')
        public static string TestBeltOf(Transform car)
        {
            Belt b = BeltOf(car);
            if (b == null) return "aucune";
            return "bouclee " + Recon.Path(b.Fastened.transform) + ", rangee " + (b.Open != null ? Recon.Path(b.Open.transform) : "-") + ", sangle conducteur " + (b.DriverOpen != null ? Recon.Path(b.DriverOpen.transform) : "-") + ", boucle " + b.DriverBuckle.ToString("F2");
        }

        // (appelee par reflexion depuis SeatExport -- ne pas renommer sans prevenir [Addons et Tools])
        static Belt BeltOf(Transform car)
        {
            Belt b;
            if (car == null) return null;
            if (belts.TryGetValue(car, out b)) return b;
            Transform f = FindUnder(car, "Fastened_mesh", null) ?? FindUnder(car, "Close", "Seatbelts");
            Transform o = FindUnder(car, "PassengerBelt", null) ?? FindUnder(car, "Passenger", "Seatbelts");
            Transform dov = FindUnder(car, "HandleUpPivot", "DriverBelt") ?? FindUnder(car, "HandleUpPivot", "Seatbelts");
            // CORRIS : pas de ceinture passager dans le jeu (Somegames1, 12/10 : « une ceinture pour le passager de la Rivett ») --
            // copie en miroir de celle du conducteur (sangles et languette), sans ses automates (ils agiraient sur la boucle et
            // la ceinture du conducteur) ; la ceinture passager du mod (BeltInput) s'en sert comme d'une vraie.
            if (o == null && f != null && dov != null && dov.parent != null && dov.parent.name == "Seatbelts" && car.name.StartsWith("CORRIS")) o = MirrorBelt(dov);
            Transform k = FindUnder(car, "SeatbeltLock", null) ?? FindUnder(car, "BuckleUp", null) ?? FindUnder(car, "HandleDownPivot", null);
            b = null;
            if (f != null && f.GetComponent<MeshFilter>() != null)
            {
                // (BACHGLOTZ : ceinture ventrale du conducteur sans sangle a languette -- Seatbelts/Open, la defaite, cachee quand
                // il est boucle chez les autres)
                Transform dopen = dov ?? FindUnder(car, "Open", "Seatbelts");
                b = new Belt { Fastened = f.gameObject, Open = o != null ? o.gameObject : null, DriverOpen = dopen != null ? dopen.gameObject : null };
                Vector3 kp = k != null ? car.InverseTransformPoint(k.position) : new Vector3(-0.2f, 0.45f, 0f);
                b.Buckle = new Vector3(-kp.x, kp.y, kp.z);   // (cote passager : en miroir)
                b.DriverBuckle = kp;
                // Attaches du conducteur (repere voiture) d'apres le modele de la ceinture bouclee : en haut du montant (point
                // le plus haut), au plancher cote portiere (le plus bas, le plus a l'exterieur).
                Mesh m = f.GetComponent<MeshFilter>().sharedMesh;
                if (m != null && m.isReadable && m.vertexCount > 0)
                {
                    Vector3[] vs = m.vertices;
                    Vector3 top = Vector3.zero, low = Vector3.zero; float minY = float.MaxValue, maxY = float.MinValue;
                    var pts = new Vector3[vs.Length];
                    for (int i = 0; i < vs.Length; i++) { pts[i] = car.InverseTransformPoint(f.TransformPoint(vs[i])); if (pts[i].y > maxY) { maxY = pts[i].y; top = pts[i]; } if (pts[i].y < minY) minY = pts[i].y; }
                    float outward = Mathf.Sign(kp.x == 0 ? -1f : top.x - kp.x), best = float.MinValue;
                    foreach (Vector3 pt in pts) if (pt.y < minY + 0.12f && pt.x * outward > best) { best = pt.x * outward; low = pt; }
                    // Bout de la languette dans le modele (le plus pres de l'axe de la voiture, en bas) : c'est lui qui doit rester
                    // sur la boucle (retour de JD, 10/10 : « la boucle ne rejoint pas l'attache » -- la sangle deformee etait tenue
                    // au point de clic de la boucle, a quelques centimetres : la languette partait avec la sangle).
                    Vector3 mb = kp; float bestIn = float.MaxValue;
                    foreach (Vector3 pt in pts) if (pt.y < minY + 0.3f && Mathf.Abs(pt.x) < bestIn) { bestIn = Mathf.Abs(pt.x); mb = pt; }
                    b.MeshBuckle = mb;
                    b.Top = top; b.Floor = low; b.Anchors = true;
                    Log.Info("ceinture : attaches de " + car.name + " haut " + top.ToString("F2") + ", plancher " + low.ToString("F2") + ", boucle " + kp.ToString("F2") + ", bout de la languette " + mb.ToString("F2") + " (" + ((mb - kp).magnitude * 100f).ToString("F0") + " cm)");
                }
                Log.Info("ceinture passager : " + car.name + " (bouclee " + f.name + ", rangee " + (o != null ? o.name : "-") + ")");
            }
            belts[car] = b;
            return b;
        }

        // Ceinture du passager avant, exactement comme celle du conducteur (automate 'Use' du jeu sur DriverBelt/.../
        // SeatbeltHandle et SeatbeltLock ; demande d'un joueur, 09/10 -- « les textes, la languette qui tourne, les sons ») :
        //  - viser la languette rangee (SeatbeltHandle de PassengerBelt) : "TAKE SEATBELT" ; clic tenu : elle est prise, rattachee
        //    a la camera la ou elle est (comme SetParent du jeu, pose gardee : elle ne tourne plus avec le regard), la sangle du
        //    jeu s'etire ; ni texte ni main pendant qu'on la tient ;
        //  - en la tenant, viser la boucle : "FASTEN SEATBELT" ; lacher la : bouclee (son seatbelt_fasten) ; lachee ailleurs, ou
        //    tiree a plus de 1,5 m du montant : elle se range (seatbelt_retract) ;
        //  - bouclee, viser la boucle : "UNFASTEN SEATBELT" ; clic : detachee (seatbelt_unfasten).
        static float beltHold;
        static void BeltInput()
        {
            if (SeatFiles.NoBelt(current.Car, current.Index)) return;   // (place « sansceinture » de l'outil des places)
            if (current.Index > 0) { RearBeltInput(); return; }
            Belt b = current.Index == 0 ? BeltOf(current.CarT) : null;
            bool aimBuckle = false, aimHandle = false;
            Transform handle = b != null && b.Open != null ? (heldHandle ?? FindUnder(b.Open.transform, "SeatbeltHandle", null)) : null;
            Transform view = Game.MainCam != null ? Game.MainCam.transform : cam;
            if (b != null && view != null)
            {
                Vector3 d = current.CarT.TransformPoint(b.Buckle) - view.position;
                aimBuckle = d.magnitude < 1.3f && Vector3.Angle(view.forward, d) < 16f;
                if (!beltOn && beltHold <= 0f && handle != null && handle.gameObject.activeInHierarchy)
                {
                    Vector3 hd = handle.position - view.position;
                    aimHandle = hd.magnitude < 1.3f && Vector3.Angle(view.forward, hd) < 14f;
                }
            }
            bool holding = beltHold > 0f;
            BlockHandle(aimHandle || aimBuckle || holding);
            bool lap = b != null && handle == null;
            BeltHint(beltOn ? (aimBuckle ? (lap ? "UNBUCKLE LAP BELT" : "UNFASTEN SEATBELT") : null) : holding ? (aimBuckle ? "FASTEN SEATBELT" : null) : aimHandle ? "TAKE SEATBELT" : null);
            if (beltOn)
            {
                if (heldHandle != null) Drop(b);   // (bouclee autrement : languette reposee)
                if (aimBuckle && Input.GetMouseButtonDown(0))
                {
                    beltOn = false;
                    Foley(lap ? "harness_lock" : "seatbelt_unfasten", current.CarT.TransformPoint(b.Buckle));
                    Log.Info("ceinture passager : detachee (" + current.Car + ")");
                    SendSeat(current.Car, current.Index, headLocal);
                }
                beltHold = 0f;
                return;
            }
            // Ceinture ventrale sans languette (BACHGLOTZ, retour de Menor, 12/10 : « des ceintures pour le passager de la
            // Bachglotz ») : comme celle du conducteur du jeu (BuckleUp), un clic sur la boucle la boucle.
            if (!holding && b != null && handle == null && aimBuckle)
            {
                BeltHint("BUCKLE UP LAPBELT");
                if (Input.GetMouseButtonDown(0))
                {
                    beltOn = true;
                    Foley("harness_lock", current.CarT.TransformPoint(b.Buckle));
                    Log.Info("ceinture passager : ventrale bouclee (" + current.Car + ")");
                    SendSeat(current.Car, current.Index, headLocal);
                }
                return;
            }
            if (!holding && aimHandle && Input.GetMouseButtonDown(0)) { Grab(b); Log.Info("ceinture passager : prise (" + current.Car + ")"); return; }
            if (!holding) return;
            Transform pivot = heldHandle != null ? heldPivot : null;
            bool tooFar = pivot != null && (heldHandle.position - pivot.position).magnitude > 1.5f;
            if ((Input.GetMouseButton(0) || testHold) && !tooFar) { beltHold += Time.deltaTime; return; }
            bool fasten = aimBuckle && !tooFar;
            Vector3 at = heldHandle != null ? heldHandle.position : current.CarT.TransformPoint(b.Buckle);
            Drop(b);
            if (fasten)
            {
                beltOn = true;
                Foley("seatbelt_fasten", current.CarT.TransformPoint(b.Buckle));
                Log.Info("ceinture passager : attachee (" + current.Car + ")");
                SendSeat(current.Car, current.Index, headLocal);
            }
            else
            {
                Foley("seatbelt_retract", at);
                Log.Info("ceinture passager : lachee " + (tooFar ? "trop loin du montant" : "hors de la boucle") + ", rangee");
            }
        }

        // Languette prise : rattachee a la camera, a la pose ou elle est (le jeu : SetParent sans remise a zero).
        static bool testHold;   // (essais : clic maintenu simule)
        static Transform heldHandle, heldPivot;
        static Vector3 heldPos, heldScale; static Quaternion heldRot;   // (echelle comprise : rattachee a la camera puis reposee, elle revenait aplatie)
        static void Grab(Belt b)
        {
            Transform h = b != null && b.Open != null ? FindUnder(b.Open.transform, "SeatbeltHandle", null) : null;
            Transform view = Game.MainCam != null ? Game.MainCam.transform : cam;
            if (h == null || view == null) return;
            if (!b.Open.activeSelf) { b.Open.SetActive(true); b.OpenHidden = false; }
            heldHandle = h; heldPivot = h.parent; heldPos = h.localPosition; heldRot = h.localRotation; heldScale = h.localScale;
            h.SetParent(view, true);
            beltHold = 0.01f;
        }
        static void Drop(Belt b)
        {
            beltHold = 0f;
            if (heldHandle == null) return;
            if (heldPivot != null) heldHandle.SetParent(heldPivot, false);
            heldHandle.localPosition = heldPos; heldHandle.localRotation = heldRot; heldHandle.localScale = heldScale;
            heldHandle = null; heldPivot = null;
        }

        // Son du jeu (groupe MasterAudio CarFoley, comme les automates de la ceinture du conducteur), a l'endroit donne.
        static readonly HashSet<string> foleyLogged = new HashSet<string>();
        static void Foley(string variation, Vector3 at)
        {
            GameObject g = GameObject.Find("MasterAudio/CarFoley/" + variation);
            AudioSource src = g != null ? g.GetComponent<AudioSource>() : null;
            if (src == null || src.clip == null) { Log.Info("ceinture : son CarFoley/" + variation + " introuvable"); return; }
            AudioSource.PlayClipAtPoint(src.clip, at, src.volume > 0f ? src.volume : 1f);
            if (foleyLogged.Add(variation)) Log.Info("ceinture : son CarFoley/" + variation + " joue");
        }

        // (pose a chaque image tant qu'on vise : les objets du jeu sous le regard -- poignee de la portiere arriere, juste
        // derriere la ceinture rangee -- effacent le texte a chaque image ; il ne restait que la main, sans "BUCKLE UP")
        // La ceinture n'a pas de collisionneur : sous le regard, c'est la poignee de la portiere arriere, juste derriere
        // (SORBET : DoorRear(right)/.../PlayerColl/Handle a 0,39 m, la ceinture a 0,32), qui prenait le clic tenu pour
        // tirer la ceinture -- portiere arriere ouverte. Coupee le temps de viser la ceinture.
        static Collider blockedHandle;
        static int handleLogs;
        static void BlockHandle(bool on)
        {
            if (!on || current == null || cam == null)
            {
                if (blockedHandle != null) blockedHandle.enabled = true;
                blockedHandle = null;
                return;
            }
            if (blockedHandle != null) return;
            foreach (RaycastHit h in Physics.RaycastAll(cam.position, cam.forward, 0.8f))
                if (h.collider.name == "Handle" && h.collider.enabled && h.collider.transform.IsChildOf(current.CarT)) { blockedHandle = h.collider; blockedHandle.enabled = false; if (handleLogs++ < 5) Log.Info("ceinture passager : poignee " + Recon.Path(h.collider.transform) + " coupee le temps de viser"); break; }
        }

        static void BeltHint(string text)
        {
            bool on = text != null;
            if (!on && !beltHint) return;
            beltHint = on;
            Game.SetGlobalBool("GUIuse", on);
            Game.SetGlobal("GUIinteraction", on ? text : "");
        }

        // Montre la ceinture bouclee (copie en miroir) des voitures dont le passager avant est attache, ici ou ailleurs.
        // Ceintures arriere (Menor, liste du 12/10, point 3) : le jeu n'en a pas ; le modele de la ceinture bouclee du
        // conducteur, porte a chaque place arriere laterale -- deplace de la place du conducteur a celle-ci, en miroir du
        // cote passager. Bouclee d'un clic sur sa boucle (pas de languette a tirer), vue par tous (avec la place). Place du
        // milieu : pas de ceinture.
        static bool RearPlace(Transform car, int index, out Vector3 offset, out bool mirror)
        {
            offset = Vector3.zero; mirror = false;
            Seat s0 = null, si = null;
            foreach (Seat s in seats) if (s.CarT == car) { if (s.Index == 0) s0 = s; if (s.Index == index) si = s; }
            if (s0 == null || si == null || index <= 0 || Mathf.Abs(si.Head.x) < 0.15f) return false;
            Vector3 driver = new Vector3(-s0.Head.x, s0.Head.y, s0.Head.z);
            mirror = si.Head.x * driver.x < 0f;
            offset = si.Head - (mirror ? s0.Head : driver);
            return true;
        }
        static Vector3 RearBuckle(Belt b, Vector3 offset, bool mirror) { return (mirror ? b.Buckle : b.DriverBuckle) + offset; }

        // Ceintures des places arriere (ajout de MWCoop, le jeu n'en a pas) : [Coop] CeinturesArriere=0 les retire -- choix de
        // l'hote, transmis aux invites par le lanceur (Menor, 25/10 : « les ceintures arriere sont moches, retirez-les »).
        public static bool RearBeltsOn { get { return Config.GetInt("Coop", "CeinturesArriere", 1) != 0; } }

        static void RearBeltInput()
        {
            if (!RearBeltsOn)
            {
                if (beltOn) { beltOn = false; SendSeat(current.Car, current.Index, headLocal); Log.Info("ceinture arriere : option coupee, detachee"); }
                return;
            }
            Belt b = BeltOf(current.CarT);
            Vector3 off; bool mir;
            if (b == null || !RearPlace(current.CarT, current.Index, out off, out mir)) return;
            Transform view = Game.MainCam != null ? Game.MainCam.transform : cam;
            if (view == null) return;
            SeatFiles.Belt fb = SeatFiles.BeltFor(current.Car, current.Index);   // (outil des places : boucle du fichier = point vise)
            Vector3 at = current.CarT.TransformPoint(fb != null && fb.HasBuckle ? fb.Buckle : RearBuckle(b, off, mir));
            Vector3 d = at - view.position;
            bool aim = d.magnitude < 1.3f && Vector3.Angle(view.forward, d) < 16f;
            BlockHandle(aim);
            BeltHint(aim ? (beltOn ? "UNFASTEN SEATBELT" : "FASTEN SEATBELT") : null);
            if (!aim || !Input.GetMouseButtonDown(0)) return;
            beltOn = !beltOn;
            Foley(beltOn ? "seatbelt_fasten" : "seatbelt_unfasten", at);
            Log.Info("ceinture arriere : " + (beltOn ? "attachee" : "detachee") + " (" + current.Car + ", place " + current.Index + ")");
            SendSeat(current.Car, current.Index, headLocal);
        }

        // (appelee par reflexion depuis SeatExport -- ne pas renommer sans prevenir [Addons et Tools])
        static void UpdateRearBelts()
        {
            var want = new Dictionary<Transform, HashSet<int>>();
            System.Action<Transform, int> add = (car, i) => { HashSet<int> h; if (!want.TryGetValue(car, out h)) want[car] = h = new HashSet<int>(); h.Add(i); };
            bool on = RearBeltsOn;   // (coupees : aucune n'est montree, ni la mienne ni celles des autres)
            if (on && current != null && current.Index > 0 && beltOn && current.CarT != null) add(current.CarT, current.Index);
            foreach (Remote r in remote.Values)
                if (on && r.Index > 0 && r.Belt)
                    foreach (Seat s in seats) if (s.Car == r.Car && s.Index == r.Index && s.CarT != null) add(s.CarT, r.Index);
            foreach (Transform car in want.Keys) BeltOf(car);
            // (SORBET : ses ceintures arriere rangees sont la meme quand personne n'est assis -- releve de la voiture)
            if (on) foreach (Seat s in seats) if (s.Index > 0 && s.CarT != null && !belts.ContainsKey(s.CarT) && HasRearModel(s.CarT)) BeltOf(s.CarT);
            foreach (KeyValuePair<Transform, Belt> kv in belts)
            {
                Belt b = kv.Value;
                if (b == null) continue;
                HashSet<int> w; want.TryGetValue(kv.Key, out w);
                if (HasRearModel(kv.Key))
                {
                    foreach (GameObject old in b.Rear.Values) if (old != null) Object.Destroy(old);
                    b.Rear.Clear();
                    UpdateRearModel(kv.Key, b, on, w);
                    continue;
                }
                var drop = new List<int>();
                foreach (KeyValuePair<int, GameObject> r in b.Rear) if (w == null || !w.Contains(r.Key)) drop.Add(r.Key);
                foreach (int i in drop) { if (b.Rear[i] != null) Object.Destroy(b.Rear[i]); b.Rear.Remove(i); }
                if (w == null || b.Fastened == null) continue;
                foreach (int i in w)
                {
                    Vector3 off; bool mir;
                    if (!RearPlace(kv.Key, i, out off, out mir)) continue;
                    bool plain = current != null && current.CarT == kv.Key && current.Index == i && !ThirdPerson.Active;
                    GameObject g; bool wasPlain;
                    if (b.Rear.TryGetValue(i, out g) && g != null && b.RearPlain.TryGetValue(i, out wasPlain) && wasPlain == plain) continue;
                    if (g != null) Object.Destroy(g);
                    g = BeltMeshCopy(b, kv.Key, mir, plain);
                    g.name = "MWCoop-CeintureArriere" + i;
                    g.transform.localPosition += off;
                    b.Rear[i] = g; b.RearPlain[i] = plain;
                }
            }
        }

        // Ceintures arriere de la SORBET : modele de Menos (25/10 ; assets/ceintures, run/ceintures-arr.py), avec la matiere de la
        // ceinture avant du jeu, dans le repere de LOD/Seatbelts (celui des ceintures avant, ou Menos a pose les siennes). Recale sur
        // les sieges du jeu (JD, 25/10 : « la sangle doit suivre le fauteuil, collee a lui, la boucle dans la pliure, des deux cotes
        // pareil ») : boucles et ancrages bas dans la pliure assise-dossier, bas de la ceinture bouclee avec eux, et la sangle rangee
        // faite ici, un ruban colle au bord avant du dossier, de la pliure a son sommet puis a l'ancrage de la plage arriere, la
        // languette en haut. Toujours la avec l'option CeinturesArriere ; a chaque place laterale la ceinture rangee, ou bouclee
        // quand un joueur y est attache (ici ou chez lui). Les autres voitures gardent la copie en miroir de la ceinture du
        // conducteur. [Test] CeinturesArriereCopie=1 : la copie aussi sur la SORBET (comme avant).
        class RearModel { public GameObject Root; public GameObject[] Open = new GameObject[2], Closed = new GameObject[2]; public int State = -1, Gen; }
        static readonly Dictionary<Transform, RearModel> rearModels = new Dictionary<Transform, RearModel>();
        static readonly string[] RearNames = { "arr-ancres-haut", "arr-boucles", "arr-ancre-bas-g", "arr-languette-g", "arr-sangle-haut-g", "arr-bouclee-g",
                                               "arr-ancre-bas-d", "arr-languette-d", "arr-sangle-haut-d", "arr-bouclee-d" };
        const int R_Top = 0, R_Buckles = 1, R_Side = 2;   // par cote (2 + 4 k) : ancrage bas, languette, sangle haute, bouclee
        static Mesh[] rearMeshes; static bool rearMeshTried;
        static Mesh[] RearMeshes()
        {
            if (rearMeshTried) return rearMeshes;
            rearMeshTried = true;
            try
            {
                rearMeshes = new Mesh[RearNames.Length];
                for (int i = 0; i < RearNames.Length; i++) rearMeshes[i] = SleepingBags.LoadMesh("MWCoop." + RearNames[i] + ".mwm", 1f);
            }
            catch (System.Exception e) { rearMeshes = null; Log.Warn("ceintures arriere : modele illisible (" + e.Message + "), copie de la ceinture avant"); }
            return rearMeshes;
        }
        // (bouclee : bombee vers l'avant au milieu des sangles -- l'avatar est plus epais que le joueur du jeu et la cachait,
        // comme la ceinture avant, BulgedMesh ; ses attaches -- epaule, boucle, bas cote portiere -- ne bougent pas)
        static void BulgeRear(Mesh m, float bulge, float reach)
        {
            if (bulge <= 0f) return;
            Vector3[] v = m.vertices;
            if (v.Length == 0) return;
            Vector3 top = v[0], inner = v[0], outer = v[0];
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (Vector3 p in v) { minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y); if (p.y > top.y) top = p; }
            float low = minY + 0.2f * (maxY - minY);
            bool lowSet = false;
            foreach (Vector3 p in v)
                if (p.y <= low)
                {
                    if (!lowSet) { inner = outer = p; lowSet = true; }
                    if (Mathf.Abs(p.x) < Mathf.Abs(inner.x)) inner = p;
                    if (Mathf.Abs(p.x) > Mathf.Abs(outer.x)) outer = p;
                }
            for (int i = 0; i < v.Length; i++)
            {
                float d = Mathf.Min((v[i] - top).magnitude, Mathf.Min((v[i] - inner).magnitude, (v[i] - outer).magnitude));
                float w = Mathf.Clamp01(d / reach);
                v[i].z += bulge * w * w * (3f - 2f * w);   // (en douceur depuis chaque attache)
            }
            m.vertices = v;
            m.RecalculateBounds();
        }

        static bool HasRearModel(Transform car)
        {
            if (car == null || Game.Msc || Config.GetInt("Test", "CeinturesArriereCopie", 0) != 0) return false;
            if (car.name.StartsWith("SORBET")) return RearMeshes() != null;
            if (car.name.StartsWith("MACHTWAGEN")) return TaxiMeshes() != null;
            return false;
        }

        // MACHTWAGEN (taxi) : modele de Menos du 25/10 (run/ceintures-arr.py, taxi-*), pose tel quel dans le repere de la voiture --
        // pas de siege separe a mesurer (sa banquette est dans LOD/interior). Toujours : ancrages, boucles et haut des sangles ;
        // rangee : bas de la sangle et languette (celle de droite en miroir, Menos n'en a fait qu'une) ; bouclee : la ceinture.
        // [Test] TaxiCeintY/Z : decalage.
        // (Base en pieces aux memes noms que la SORBET -- AncresHaut, Boucles, AncreBasG/D : l'outil des places les deplace une a une)
        static readonly string[] TaxiNames = { "taxi-ancres-haut", "taxi-sangle-haut-g", "taxi-sangle-bas-g", "taxi-languette-g", "taxi-bouclee-g",
                                               "taxi-sangle-haut-d", "taxi-sangle-bas-d", "taxi-languette-d", "taxi-bouclee-d",
                                               "taxi-boucles", "taxi-ancre-bas-g", "taxi-ancre-bas-d" };
        static Mesh[] taxiMeshes; static bool taxiMeshTried;
        static Mesh[] TaxiMeshes()
        {
            if (taxiMeshTried) return taxiMeshes;
            taxiMeshTried = true;
            try
            {
                taxiMeshes = new Mesh[TaxiNames.Length];
                for (int i = 0; i < TaxiNames.Length; i++) taxiMeshes[i] = SleepingBags.LoadMesh("MWCoop." + TaxiNames[i] + ".mwm", 1f);
            }
            catch (System.Exception e) { taxiMeshes = null; Log.Warn("ceintures arriere du taxi : modele illisible (" + e.Message + ")"); }
            return taxiMeshes;
        }
        static RearModel BuildTaxiModel(Transform car, Belt b)
        {
            Mesh[] ms = TaxiMeshes();
            MeshRenderer src = b.Fastened != null ? b.Fastened.GetComponent<MeshRenderer>() : null;
            if (ms == null || src == null) return null;
            var m = new RearModel { Root = new GameObject("MWCoop-CeinturesArriere") };
            Transform rt = m.Root.transform;
            rt.parent = car;
            rt.localPosition = new Vector3(0f, Config.GetFloat("Test", "TaxiCeintY", 0f), Config.GetFloat("Test", "TaxiCeintZ", 0f));
            rt.localRotation = Quaternion.identity;
            Vector3 cs = car.lossyScale;
            rt.localScale = new Vector3(1f / cs.x, 1f / cs.y, 1f / cs.z);
            System.Func<string, Mesh, Transform, GameObject> part = (name, mesh, parent) =>
            {
                var g = new GameObject(name);
                g.layer = b.Fastened.layer;
                g.transform.parent = parent;
                g.transform.localPosition = Vector3.zero; g.transform.localRotation = Quaternion.identity; g.transform.localScale = Vector3.one;
                if (mesh != null) { g.AddComponent<MeshFilter>().sharedMesh = mesh; g.AddComponent<MeshRenderer>().sharedMaterial = src.sharedMaterial; }
                return g;
            };
            Transform baseT = part("Base", null, rt).transform;
            part("AncresHaut", ms[0], baseT); part("Boucles", ms[9], baseT); part("AncreBasG", ms[10], baseT); part("AncreBasD", ms[11], baseT);
            float bulge = Config.GetFloat("Test", "CeintureArrBombe", 0.04f), reach = Config.GetFloat("Test", "CeintureArrRayon", 0.28f);
            for (int k = 0; k < 2; k++)
            {
                int o = 1 + 4 * k;
                part(k == 0 ? "SangleHauteG" : "SangleHauteD", ms[o], baseT);
                m.Open[k] = part(k == 0 ? "RangeeG" : "RangeeD", null, rt);
                part("Sangle", ms[o + 1], m.Open[k].transform);
                part("Languette", ms[o + 2], m.Open[k].transform);
                m.Closed[k] = part(k == 0 ? "BoucleeG" : "BoucleeD", null, rt);
                Mesh fast = Object.Instantiate(ms[o + 3]);
                BulgeRear(fast, bulge, reach);
                part("Ceinture", fast, m.Closed[k].transform);
            }
            Log.Info("ceintures arriere : modele de Menos pose sur " + car.name + " (taxi, matiere " + (src.sharedMaterial != null ? src.sharedMaterial.name : "?") + ")");
            return m;
        }
        static void UpdateRearModel(Transform car, Belt b, bool on, HashSet<int> belted)
        {
            RearModel m; rearModels.TryGetValue(car, out m);
            if (!on) { if (m != null && m.Root != null && m.Root.activeSelf) m.Root.SetActive(false); return; }
            if (m != null && m.Root != null && m.Gen != SeatFiles.Generation) { Object.Destroy(m.Root); m = null; }   // (fichier des places relu : refaite)
            if (m == null || m.Root == null)
            {
                m = car.name.StartsWith("MACHTWAGEN") ? BuildTaxiModel(car, b) : BuildRearModel(car, b);
                if (m != null) { m.Gen = SeatFiles.Generation; FileBelts(car, m); }
                rearModels[car] = m;
            }
            if (m == null) return;
            if (!m.Root.activeSelf) m.Root.SetActive(true);
            int state = 0;
            if (belted != null)
                foreach (int i in belted)
                    foreach (Seat s in seats)
                        if (s.CarT == car && s.Index == i && Mathf.Abs(s.Head.x) >= 0.15f) state |= s.Head.x < 0f ? 1 : 2;
            if (state == m.State) return;
            m.State = state;
            for (int k = 0; k < 2; k++)
            {
                bool fast = (state & (1 << k)) != 0;
                m.Open[k].SetActive(!fast); m.Closed[k].SetActive(fast);
            }
        }

        // Ceintures du fichier des places (outil de [Addons et Tools], 26/10 ; JD : « Tout me vas ») : par cote, celles de la place
        // arriere de ce cote. Points dans le repere de la voiture (CarT) ; une ligne absente garde la pose calculee. Pieces (ancrages,
        // boucle, languette) : centre de leurs bornes (cote de la place) mis au point du fichier, rotation en plus autour de lui ;
        // trajets rangee / bouclee : ruban de 'largeur' a la place de la sangle du modele.
        static void FileBelts(Transform car, RearModel m)
        {
            Transform rt = m.Root.transform, baseT = rt.Find("Base");
            var diag = new System.Text.StringBuilder();
            for (int k = 0; k < 2; k++)
            {
                float sx = k == 0 ? -1f : 1f;
                Seat s = null;
                foreach (Seat q in seats) if (q.CarT == car && q.Index > 0 && Mathf.Abs(q.Head.x) >= 0.15f && Mathf.Sign(q.Head.x) == sx) { s = q; break; }
                SeatFiles.Belt fb = s != null ? SeatFiles.BeltFor(s.Car, s.Index) : null;
                if (fb == null) continue;
                string sd = k == 0 ? "G" : "D";
                System.Func<Vector3, Vector3> at = v => rt.InverseTransformPoint(car.TransformPoint(v));
                int moved = 0;
                if (fb.HasTop && MovePart(baseT, "AncresHaut", sx, at(fb.Top), Quaternion.identity)) moved++;
                if (fb.HasBottom && MovePart(baseT, "AncreBas" + sd, 0f, at(fb.Bottom), Quaternion.identity)) moved++;
                if (fb.HasBuckle && MovePart(baseT, "Boucles", sx, at(fb.Buckle), fb.BuckleRot)) moved++;
                if (fb.HasTongue && MovePart(m.Open[k].transform, "Languette", 0f, at(fb.Tongue), fb.TongueRot)) moved++;
                // (texture de la sangle : sangle haute du modele, sous la bouclee (SORBET) ou sous Base (taxi))
                Transform sh = m.Closed[k].transform.Find("SangleHaute") ?? (baseT != null ? baseT.Find("SangleHaute" + sd) : null);
                Mesh uv = sh != null && sh.GetComponent<MeshFilter>() != null ? sh.GetComponent<MeshFilter>().sharedMesh : null;
                List<Vector3> st = fb.StowedPath(), fp = fb.FastenedPath();
                bool rib = false;
                if (uv != null && st.Count >= 2) rib |= StrapTo(m.Open[k].transform, "Sangle", st.ConvertAll(v => at(v)), fb.Width, uv);
                if (uv != null && fp.Count >= 2 && StrapTo(m.Closed[k].transform, "Ceinture", fp.ConvertAll(v => at(v)), fb.Width, uv))
                {
                    rib = true;
                    if (sh.parent == m.Closed[k].transform) sh.gameObject.SetActive(false);
                }
                // (taxi : sa sangle haute est montree dans les deux etats -- le fichier donne tout le trajet, elle ferait double)
                if (rib && sh != null && sh.parent == baseT && fb.HasTop) sh.gameObject.SetActive(false);
                diag.Append(" ; ").Append(sd).Append(" place ").Append(s.Index).Append(" (").Append(fb.Kind).Append(") : ").Append(moved).Append(" pieces, rangee ")
                    .Append(st.Count).Append(" points, bouclee ").Append(fp.Count).Append(" points");
            }
            if (diag.Length > 0) Log.Info("ceintures arriere : fichier des places sur " + car.name + diag);
        }
        // Piece 'name' sous 'parent' (sx != 0 : seulement ses sommets de ce cote, les maillages des deux cotes) : centre de ses bornes
        // en 'to', tournee de 'rot' autour de lui. Copie du maillage (ceux des ressources sont partages).
        static bool MovePart(Transform parent, string name, float sx, Vector3 to, Quaternion rot)
        {
            Transform t = parent != null ? parent.Find(name) : null;
            MeshFilter mf = t != null ? t.GetComponent<MeshFilter>() : null;
            if (mf == null || mf.sharedMesh == null) return false;
            Mesh msh = Object.Instantiate(mf.sharedMesh);
            Vector3[] v = msh.vertices, nr = msh.normals;
            Vector3 lo = Vector3.one * float.MaxValue, hi = -lo;
            for (int i = 0; i < v.Length; i++) if (sx == 0f || Mathf.Sign(v[i].x) == sx) { lo = Vector3.Min(lo, v[i]); hi = Vector3.Max(hi, v[i]); }
            if (lo.x > hi.x) return false;
            Vector3 c = (lo + hi) / 2f;
            for (int i = 0; i < v.Length; i++)
                if (sx == 0f || Mathf.Sign(v[i].x) == sx) { v[i] = to + rot * (v[i] - c); if (i < nr.Length) nr[i] = rot * nr[i]; }
            msh.vertices = v; if (nr.Length == v.Length) msh.normals = nr;
            msh.RecalculateBounds();
            mf.sharedMesh = msh;
            return true;
        }
        // Ruban le long de 'path' (largeur 'w', a plat face a l'avant de la voiture) a la place du maillage de 'name' sous 'parent'.
        static bool StrapTo(Transform parent, string name, List<Vector3> path, float w, Mesh uv)
        {
            Transform t = parent.Find(name);
            MeshFilter mf = t != null ? t.GetComponent<MeshFilter>() : null;
            if (mf == null) return false;
            var a = new List<Vector3>(); var c = new List<Vector3>();
            Vector3 prev = Vector3.zero;
            for (int i = 0; i < path.Count; i++)
            {
                Vector3 tg = path[Mathf.Min(i + 1, path.Count - 1)] - path[Mathf.Max(i - 1, 0)];
                Vector3 side = Vector3.Cross(tg, Vector3.forward);
                side = side.sqrMagnitude > 1e-8f ? side.normalized : Vector3.right;
                if (i > 0 && Vector3.Dot(side, prev) < 0f) side = -side;
                prev = side;
                a.Add(path[i] - side * (w / 2f)); c.Add(path[i] + side * (w / 2f));
            }
            Mesh r = Ribbon(a, c, uv);
            if (r == null) return false;
            mf.sharedMesh = r;
            return true;
        }

        // Sommets d'un maillage de la voiture, dans le repere 'into'.
        static Vector3[] VertsIn(Transform car, string name, Transform into)
        {
            Transform t = FindUnder(car, name, null);
            MeshFilter mf = t != null ? t.GetComponent<MeshFilter>() : null;
            if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) return null;
            Vector3[] v = mf.sharedMesh.vertices;
            for (int i = 0; i < v.Length; i++) v[i] = into.InverseTransformPoint(t.TransformPoint(v[i]));
            return v;
        }
        static Vector3 Center(Vector3[] v, int side)
        {
            Vector3 c = Vector3.zero; int n = 0;
            foreach (Vector3 p in v) if (side == 0 || Mathf.Sign(p.x) == side) { c += p; n++; }
            return n > 0 ? c / n : Vector3.zero;
        }
        static Mesh Shifted(Mesh src, System.Func<Vector3, Vector3> f)
        {
            Mesh m = Object.Instantiate(src);
            Vector3[] v = m.vertices;
            for (int i = 0; i < v.Length; i++) v[i] = f(v[i]);
            m.vertices = v; m.RecalculateBounds();
            return m;
        }

        static RearModel BuildRearModel(Transform car, Belt b)
        {
            Mesh[] ms = RearMeshes();
            MeshRenderer src = b.Fastened != null ? b.Fastened.GetComponent<MeshRenderer>() : null;
            if (ms == null || src == null) return null;
            var m = new RearModel { Root = new GameObject("MWCoop-CeinturesArriere") };
            Transform rt = m.Root.transform;
            rt.parent = car;
            Transform sbt = FindUnder(car, "Seatbelts", "LOD");
            rt.localPosition = sbt != null ? car.InverseTransformPoint(sbt.position) : new Vector3(0f, -0.09f, 0.24f);
            rt.localRotation = Quaternion.identity;
            Vector3 cs = car.lossyScale;
            rt.localScale = new Vector3(1f / cs.x, 1f / cs.y, 1f / cs.z);
            System.Func<string, Mesh, Transform, GameObject> part = (name, mesh, parent) =>
            {
                var g = new GameObject(name);
                g.layer = b.Fastened.layer;
                g.transform.parent = parent;
                g.transform.localPosition = Vector3.zero; g.transform.localRotation = Quaternion.identity; g.transform.localScale = Vector3.one;
                if (mesh != null)
                {
                    g.AddComponent<MeshFilter>().sharedMesh = mesh;
                    g.AddComponent<MeshRenderer>().sharedMaterial = src.sharedMaterial;
                }
                return g;
            };
            // Sieges du jeu dans le repere du modele : dossier (RearSeat/Pivot/seat_rear_bench), assise (LOD/seat_rear_seat).
            Vector3[] bench = VertsIn(car, "seat_rear_bench", rt), cush = VertsIn(car, "seat_rear_seat", rt);
            Transform bt = FindUnder(car, "seat_rear_bench", null);
            int[] benchTri = bench != null && bt != null ? bt.GetComponent<MeshFilter>().sharedMesh.triangles : null;
            float lift = Config.GetFloat("Test", "CeinturesArriereY", 0f), fwd = Config.GetFloat("Test", "CeinturesArriereZ", 0f);
            var diag = new System.Text.StringBuilder();
            Vector3 bshiftG = Vector3.zero, bshiftD = Vector3.zero;
            Vector3[] ashift = new Vector3[2];
            var ribbons = new Mesh[2]; var tongue = new Mesh[2];
            // (ancrages hauts : sur la plage arriere, derriere le haut du dossier -- dans le repere de Menos ils sont 18 cm plus haut et
            // 24 cm plus en arriere que sur la SORBET du jeu, le meme ecart que ses boucles ; la sangle montait vers le plafond. JD,
            // 25/10 : « comme d'origine, repose sur le dossier et va vers le coffre ». [Test] AncreHautY/Z)
            Vector3 topShift = new Vector3(0f, Config.GetFloat("Test", "AncreHautY", -0.18f), Config.GetFloat("Test", "AncreHautZ", -0.24f));
            if (bench != null && cush != null)
            {
                float benchTop = float.MinValue, cushBack = float.MaxValue, outer = 0f;
                foreach (Vector3 p in bench) { benchTop = Mathf.Max(benchTop, p.y); outer = Mathf.Max(outer, Mathf.Abs(p.x)); }
                foreach (Vector3 p in cush) cushBack = Mathf.Min(cushBack, p.z);
                // pliure en x : haut de l'assise (sa partie arriere) et avant du dossier juste au-dessus
                System.Func<float, Vector2> crease = xq =>
                {
                    float y = float.MinValue, z = float.MinValue;
                    foreach (Vector3 p in cush) if (Mathf.Abs(p.x - xq) < 0.08f && p.z < cushBack + 0.15f) y = Mathf.Max(y, p.y);
                    if (y == float.MinValue) y = 0f;
                    foreach (Vector3 p in bench) if (Mathf.Abs(p.x - xq) < 0.08f && p.y < y + 0.1f) z = Mathf.Max(z, p.z);
                    if (z == float.MinValue) z = cushBack;
                    return new Vector2(y, z);
                };
                Vector3[] buckles = ms[R_Buckles].vertices;
                for (int k = 0; k < 2; k++)
                {
                    float sx = k == 0 ? -1f : 1f;
                    // boucle : son bas dans la pliure, son avant 2 cm devant le dossier
                    float bMinY = float.MaxValue, bMaxZ = float.MinValue, bx = 0f; int bn = 0;
                    foreach (Vector3 p in buckles) if (Mathf.Sign(p.x) == sx) { bMinY = Mathf.Min(bMinY, p.y); bMaxZ = Mathf.Max(bMaxZ, p.z); bx += p.x; bn++; }
                    bx = bn > 0 ? bx / bn : sx * 0.18f;
                    Vector2 cb = crease(bx);
                    Vector3 bs = new Vector3(0f, cb.x - 0.01f - bMinY + lift, cb.y + 0.02f - bMaxZ + fwd);
                    if (k == 0) bshiftG = bs; else bshiftD = bs;
                    // bord avant du dossier, cote portiere, de la pliure a son sommet (un point tous les 3 cm)
                    var edge = new List<Vector3>();
                    Vector2 co = crease(sx * (outer - 0.06f));
                    // (rails de la sangle sur la face avant du dossier, a 5 et 9,5 cm de son bord a cette hauteur : rayons sur ses
                    // triangles ; le bord est arrondi, le sommet le plus en avant mettait la sangle dans le rembourrage)
                    float off = Config.GetFloat("Test", "SangleAvance", 0.008f), in0 = Config.GetFloat("Test", "SangleBord", 0.05f);
                    var rail2 = new List<Vector3>();
                    for (float y = co.x + 0.02f; y <= benchTop - 0.01f; y += 0.03f)
                    {
                        float ox = 0f;
                        foreach (Vector3 p in bench) if (Mathf.Abs(p.y - y) < 0.05f) ox = Mathf.Max(ox, sx * p.x);
                        if (ox <= 0f) continue;
                        float za, zb;
                        if (!SurfaceZ(bench, benchTri, sx * (ox - in0), y, out za) || !SurfaceZ(bench, benchTri, sx * (ox - in0 - 0.045f), y, out zb)) continue;
                        edge.Add(new Vector3(sx * (ox - in0), y + lift, za + off + fwd));
                        rail2.Add(new Vector3(sx * (ox - in0 - 0.045f), y + lift, zb + off + fwd));
                    }
                    // ancrage bas : au pied du bord du dossier, dans la pliure
                    Vector3 ac = Center(ms[R_Side + 4 * k].vertices, 0);
                    Vector3 foot = edge.Count > 0 ? edge[0] : new Vector3(sx * (outer - 0.05f), co.x, co.y);
                    ashift[k] = new Vector3(foot.x - sx * 0.03f - ac.x, co.x + 0.02f - ac.y + lift, co.y + 0.015f - ac.z + fwd);
                    // haut : l'ancrage de la plage arriere
                    Vector3 anchor = Center(ms[R_Top].vertices, (int)sx) + topShift;
                    // (lisses : le dossier du jeu a peu de faces, la sangle faisait des dents de scie)
                    Smooth(edge, 3); Smooth(rail2, 3);
                    if (edge.Count > 0) { edge.Add(anchor + new Vector3(0f, -0.02f, 0.03f)); rail2.Add(anchor + new Vector3(-sx * 0.045f, -0.02f, 0.03f)); }
                    ribbons[k] = Ribbon(edge, rail2, ms[R_Side + 2 + 4 * k]);
                    // languette : sur le ruban, 7 cm sous le haut du dossier
                    Vector3 tc = Center(ms[R_Side + 1 + 4 * k].vertices, 0), at = foot;
                    foreach (Vector3 q in edge) if (q.y <= benchTop - 0.07f) at = q;
                    tongue[k] = Shifted(ms[R_Side + 1 + 4 * k], q => q + (at + new Vector3(-sx * 0.02f, 0f, 0.012f) - tc));
                    diag.Append(k == 0 ? " ; gauche" : " ; droite").Append(" pliure (y ").Append(co.x.ToString("F2")).Append(", z ").Append(co.y.ToString("F2"))
                        .Append("), bord ").Append(edge.Count).Append(" points, boucle ").Append(bs.ToString("F2")).Append(", ancrage bas ").Append(ashift[k].ToString("F2"));
                }
            }
            else Log.Warn("ceintures arriere : sieges de la SORBET illisibles, modele tel quel");
            Vector3 bG = bshiftG, bD = bshiftD;
            float bulge = Config.GetFloat("Test", "CeintureArrBombe", 0.04f), reach = Config.GetFloat("Test", "CeintureArrRayon", 0.28f);
            Transform baseT = part("Base", null, rt).transform;
            Vector3 ts = topShift;
            part("AncresHaut", Shifted(ms[R_Top], q => q + ts), baseT);
            part("Boucles", Shifted(ms[R_Buckles], q => q + (q.x < 0f ? bG : bD)), baseT);
            for (int k = 0; k < 2; k++)
            {
                Vector3 a = ashift[k], bk = k == 0 ? bG : bD;
                part(k == 0 ? "AncreBasG" : "AncreBasD", Shifted(ms[R_Side + 4 * k], q => q + a), baseT);
                m.Open[k] = part(k == 0 ? "RangeeG" : "RangeeD", null, rt);
                if (ribbons[k] != null)
                {
                    GameObject rg = part("Sangle", ribbons[k], m.Open[k].transform);
                    if (Config.GetInt("Test", "RubanRouge", 0) != 0) rg.GetComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Diffuse")) { color = Color.red };   // (essais)
                    diag.Append(k == 0 ? " ; ruban g " : " ; ruban d ").Append(car.InverseTransformPoint(rt.TransformPoint(ribbons[k].bounds.center)).ToString("F2")).Append(" taille ").Append(ribbons[k].bounds.size.ToString("F2"));
                }
                part("Languette", tongue[k] ?? ms[R_Side + 1 + 4 * k], m.Open[k].transform);
                m.Closed[k] = part(k == 0 ? "BoucleeG" : "BoucleeD", null, rt);
                // (sangle de l'ancrage haut a l'epaule : son bout cote ancrage suit l'ancrage, celui de l'epaule ne bouge pas)
                Mesh sh = ms[R_Side + 2 + 4 * k];
                float shBack = sh.bounds.min.z, shFront = sh.bounds.max.z;
                part("SangleHaute", Shifted(sh, q => q + ts * Mathf.Clamp01((shFront - q.z) / Mathf.Max(0.01f, shFront - shBack))), m.Closed[k].transform);
                // bouclee : son bas suit boucle (cote milieu) et ancrage bas (cote portiere), son haut (l'epaule) ne bouge pas
                Mesh fm = ms[R_Side + 3 + 4 * k];
                float minY = fm.bounds.min.y, maxY = fm.bounds.max.y, xin = 0.18f, xout = 0.62f;
                Mesh fast = Shifted(fm, q =>
                {
                    float w = 1f - Mathf.Clamp01((q.y - minY) / Mathf.Max(0.01f, maxY - minY));
                    float tt = Mathf.Clamp01((Mathf.Abs(q.x) - xin) / (xout - xin));
                    return q + Vector3.Lerp(bk, a, tt) * w;
                });
                BulgeRear(fast, bulge, reach);
                part("Ceinture", fast, m.Closed[k].transform);
            }
            Log.Info("ceintures arriere : modele de Menos pose sur " + car.name + " (matiere " + (src.sharedMaterial != null ? src.sharedMaterial.name : "?") + ")" + diag);
            return m;
        }

        // Moyenne glissante sur trois points, les bouts gardes, 'passes' fois.
        static void Smooth(List<Vector3> l, int passes)
        {
            for (int p = 0; p < passes; p++)
            {
                if (l.Count < 3) return;
                var c = new List<Vector3>(l);
                for (int i = 1; i < l.Count - 1; i++) l[i] = (c[i - 1] + c[i] * 2f + c[i + 1]) / 4f;
            }
        }

        // Avant de la surface (z le plus grand) du maillage (sommets, triangles) en (x, y) : rayon le long de z.
        static bool SurfaceZ(Vector3[] v, int[] tri, float x, float y, out float z)
        {
            z = float.MinValue;
            if (tri == null) return false;
            for (int i = 0; i + 2 < tri.Length; i += 3)
            {
                Vector3 a = v[tri[i]], b = v[tri[i + 1]], c = v[tri[i + 2]];
                float d = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
                if (Mathf.Abs(d) < 1e-9f) continue;
                float l1 = ((b.y - c.y) * (x - c.x) + (c.x - b.x) * (y - c.y)) / d, l2 = ((c.y - a.y) * (x - c.x) + (a.x - c.x) * (y - c.y)) / d, l3 = 1f - l1 - l2;
                if (l1 < 0f || l2 < 0f || l3 < 0f) continue;
                z = Mathf.Max(z, l1 * a.z + l2 * b.z + l3 * c.z);
            }
            return z > float.MinValue;
        }

        // Sangle rangee : ruban de 4,5 cm le long de 'path' (du bas vers le haut), vers l'interieur du siege depuis le bord, visible
        // des deux faces ; teinte d'un point de la sangle du modele.
        static Mesh Ribbon(List<Vector3> path, List<Vector3> path2, Mesh strap)
        {
            if (path.Count < 2 || path2.Count != path.Count) return null;
            // (coordonnees de texture de la sangle du modele : sa largeur d'un rail a l'autre, au milieu de sa longueur)
            Vector2[] su = strap.uv;
            Vector2 lo = su.Length > 0 ? su[0] : Vector2.zero, hi = lo;
            foreach (Vector2 q in su) { lo = Vector2.Min(lo, q); hi = Vector2.Max(hi, q); }
            bool alongU = hi.x - lo.x > hi.y - lo.y;   // (la longueur de la sangle suit u ou v dans la texture)
            Vector2 mid = (lo + hi) / 2f;
            Vector2 uvA = alongU ? new Vector2(mid.x, lo.y) : new Vector2(lo.x, mid.y), uvB = alongU ? new Vector2(mid.x, hi.y) : new Vector2(hi.x, mid.y);
            int n = path.Count;
            var v = new Vector3[n * 4]; var nr = new Vector3[n * 4]; var u = new Vector2[n * 4];
            var tri = new List<int>();
            for (int i = 0; i < n; i++)
            {
                Vector3 a = path[i], c = path2[i];
                v[2 * i] = a; v[2 * i + 1] = c; v[2 * n + 2 * i] = a; v[2 * n + 2 * i + 1] = c;
                nr[2 * i] = nr[2 * i + 1] = Vector3.forward; nr[2 * n + 2 * i] = nr[2 * n + 2 * i + 1] = Vector3.back;
                u[2 * i] = u[2 * n + 2 * i] = uvA; u[2 * i + 1] = u[2 * n + 2 * i + 1] = uvB;
                if (i == 0) continue;
                int p0 = 2 * (i - 1), p1 = p0 + 1, q0 = 2 * i, q1 = q0 + 1;
                tri.AddRange(new[] { p0, q0, p1, p1, q0, q1 });
                tri.AddRange(new[] { 2 * n + p0, 2 * n + p1, 2 * n + q0, 2 * n + p1, 2 * n + q1, 2 * n + q0 });
            }
            var m = new Mesh { name = "MWCoop-SangleArriere" };
            m.vertices = v; m.normals = nr; m.uv = u; m.triangles = tri.ToArray();
            m.RecalculateBounds();
            return m;
        }

        static void UpdateBelts()
        {
            UpdateRearBelts();
            var want = new HashSet<Transform>();
            if (current != null && current.Index == 0 && beltOn) want.Add(current.CarT);
            foreach (Remote r in remote.Values)
                if (r.Index == 0 && r.Belt)
                    foreach (Seat s in seats) if (s.Car == r.Car && s.Index == 0 && s.CarT != null) want.Add(s.CarT);
            var wantDriver = new HashSet<Transform>();
            foreach (KeyValuePair<int, bool> kv in driverBelt)
                if (kv.Value) { Transform t = VehicleSync.RemoteCarTransform(kv.Key); if (t != null) wantDriver.Add(t); }
            foreach (Transform car in want) BeltOf(car);
            foreach (Transform car in wantDriver) BeltOf(car);
            foreach (KeyValuePair<Transform, Belt> kv in belts)
            {
                Belt b = kv.Value;
                if (b == null) continue;
                bool on = kv.Key != null && want.Contains(kv.Key);
                // Le passager d'ici (premiere personne) voit la ceinture comme le conducteur du jeu voit la sienne : le modele
                // tel quel (fait pour la vue des yeux), pas celui deforme autour de l'avatar que voient les autres (demande de
                // JD, 10/10). A la troisieme personne : celle de l'avatar.
                bool plain = on && current != null && current.CarT == kv.Key && current.Index == 0 && beltOn && !ThirdPerson.Active;
                if (on && b.Copy != null && b.CopyPlain != plain) { Object.Destroy(b.Copy); b.Copy = null; }
                if (on && b.Copy == null && b.Fastened != null) { b.Copy = BeltMeshCopy(b, kv.Key, true, plain); b.CopyPlain = plain; }
                else if (!on && b.Copy != null) { Object.Destroy(b.Copy); b.Copy = null; }
                if (b.Open != null && on != b.OpenHidden) { b.Open.SetActive(!on); b.OpenHidden = on; }
                // Conducteur distant boucle : copie (sans miroir) et sangle pendante cachee (rendue si c'est nous qui l'avions cachee).
                bool don = kv.Key != null && wantDriver.Contains(kv.Key);
                if (don && b.DriverCopy == null && b.Fastened != null) b.DriverCopy = BeltCopy(b, kv.Key, false);
                else if (!don && b.DriverCopy != null) { Object.Destroy(b.DriverCopy); b.DriverCopy = null; }
                if (b.DriverOpen != null)
                {
                    if (don && !b.DriverOpenHidden && b.DriverOpen.activeSelf) { b.DriverOpen.SetActive(false); b.DriverOpenHidden = true; }
                    else if (!don && b.DriverOpenHidden) { b.DriverOpen.SetActive(true); b.DriverOpenHidden = false; }
                }
            }
        }

        // Ceinture bouclee montree (passager, ou conducteur distant) : le modele de la ceinture bouclee du jeu (Fastened_mesh :
        // sangle droite, boucle au bout), en miroir pour le passager, a sa place exacte -- ses bouts sur leurs attaches, quitte a
        // traverser le torse de l'avatar (demande de JD, 09/10). (Avant : decalee de 17 cm vers l'avant, elle flottait ; puis
        // des brins dessines, refuses : « la meme que la place conducteur ».)
        static GameObject BeltCopy(Belt b, Transform car, bool mirror) { return BeltMeshCopy(b, car, mirror, false); }

        // Copie du modele : en miroir par le plan median de la voiture (x -> -x) pour le passager, telle quelle pour le conducteur.
        static GameObject BeltMeshCopy(Belt b, Transform car, bool mirror, bool plain)
        {
            Transform src = b.Fastened.transform;
            GameObject c = new GameObject(mirror ? "MWCoop-CeinturePassager" : "MWCoop-CeintureConducteur");
            c.layer = b.Fastened.layer;
            c.transform.parent = car;
            Vector3 p = car.InverseTransformPoint(src.position);
            Quaternion q = Quaternion.Inverse(car.rotation) * src.rotation;
            float fwd; if (!float.TryParse(Config.Get("Test", "CeintureAvance", ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fwd)) fwd = BeltForward;
            Vector3 sc = src.lossyScale, cs = car.lossyScale;
            c.transform.localPosition = new Vector3(mirror ? -p.x : p.x, p.y, p.z + fwd);
            c.transform.localRotation = mirror ? new Quaternion(q.x, -q.y, -q.z, q.w) : q;
            c.transform.localScale = new Vector3((mirror ? -sc.x : sc.x) / cs.x, sc.y / cs.y, sc.z / cs.z);
            c.AddComponent<MeshFilter>().sharedMesh = plain ? src.GetComponent<MeshFilter>().sharedMesh : BulgedMesh(b, car, src);
            MeshRenderer mr = src.GetComponent<MeshRenderer>();
            MeshRenderer cr = c.AddComponent<MeshRenderer>();
            if (mr != null) cr.sharedMaterials = mr.sharedMaterials;
            return c;
        }

        // Le modele deforme au milieu de chaque sangle, pas a ses bouts (haut du montant, plancher et boucle restent sur leurs
        // attaches) : l'avatar est plus epais que le joueur du jeu et la cachait toute entiere, et la diagonale du jeu descend
        // raide le long du cote portiere du torse. Diagonale : avancee devant la poitrine et ramenee vers le milieu (de l'epaule
        // a la boucle) ; sangle du bassin : a peine avancee. Calcule une fois par voiture (memes poids pour la copie en miroir).
        // Modele illisible : tel quel.
        static Mesh BulgedMesh(Belt b, Transform car, Transform src)
        {
            Mesh m = src.GetComponent<MeshFilter>().sharedMesh;
            if (b.Bulged != null) return b.Bulged;
            float bulge = Config.GetFloat("Test", "CeintureBombe", 0.26f), lap = Config.GetFloat("Test", "CeintureBassin", 0.1f),
                  inward = Config.GetFloat("Test", "CeintureMilieu", 0.1f), radius = Config.GetFloat("Test", "CeintureRayon", 0.3f);
            if (!b.Anchors || bulge <= 0f) return m;
            try
            {
                Vector3[] v = m.vertices;
                Vector3 fwd = src.InverseTransformVector(car.TransformVector(Vector3.forward));
                Vector3 side = src.InverseTransformVector(car.TransformVector(Vector3.right * Mathf.Sign(b.MeshBuckle.x - b.Top.x)));
                for (int i = 0; i < v.Length; i++)
                {
                    Vector3 cp = car.InverseTransformPoint(src.TransformPoint(v[i]));
                    // (repere du conducteur : le modele source n'est pas en miroir ; la languette et ses 8 cm ne bougent pas)
                    Vector3 mb = b.MeshBuckle;
                    float dB = Mathf.Max(0f, Vector3.Distance(cp, mb) - Config.GetFloat("Test", "CeintureLanguette", 0.08f));
                    float d = Mathf.Min(Vector3.Distance(cp, b.Top), Mathf.Min(Vector3.Distance(cp, b.Floor), dB));
                    float w = Mathf.SmoothStep(0f, 1f, d / radius), t = Mathf.Clamp01((cp.y - mb.y - 0.05f) / 0.15f);   // t : 1 sur la diagonale, 0 sur la sangle du bassin
                    v[i] += fwd * (Mathf.Lerp(lap, bulge, t) * w) + side * (inward * t * w);
                }
                Mesh n = Object.Instantiate(m);
                n.vertices = v;
                n.RecalculateBounds();
                b.Bulged = n;
                return n;
            }
            catch (System.Exception e) { Log.Info("ceinture : modele non bombe (" + e.Message + ")"); return m; }
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            string car = r.Str();
            int raw = r.U8();
            if (raw == DriverBeltIndex)
            {
                bool db = r.U8() != 0;
                if (Session.IsHost) Session.Broadcast(new NetWriter(Msg.Seat).U8(who).Str(car).U8(DriverBeltIndex).U8(db ? 1 : 0), true, who);
                bool was;
                if (!driverBelt.TryGetValue(who, out was) || was != db) Log.Info("ceinture conducteur : #" + who + (db ? " bouclee" : " detachee") + " (" + car + ")");
                driverBelt[who] = db;
                return;
            }
            int index = raw - 1;
            Vector3 head = r.Vec();
            bool belt = r.More && r.U8() != 0;
            if (Session.IsHost) Session.Broadcast(new NetWriter(Msg.Seat).U8(who).Str(car).U8(index + 1).Vec(head).U8(belt ? 1 : 0), true, who);
            Remote old;
            bool had = remote.TryGetValue(who, out old);
            if (index < 0) { remote.Remove(who); if (had) Log.Info("passager : #" + who + " est sorti de " + old.Car); return; }
            remote[who] = new Remote { Car = car, Index = index, Head = head, Belt = belt };
            if (!had || old.Car != car || old.Index != index) Log.Info("passager : #" + who + " assis dans " + car + " (place " + index + ")");
            if (had && old.Belt != belt) Log.Info("ceinture passager : #" + who + (belt ? " attache" : " detache"));
        }

        public static void PlayerLeft(int id) { remote.Remove(id); driverBelt.Remove(id); }
        // Sens de la place d'un passager distant (degres, repere de la voiture ; 0 : l'avant).
        // Place debout (cabine du tracteur) d'un passager distant : avatar debout, pas assis.
        public static bool RemoteSeatStanding(int id)
        {
            Remote r;
            if (id == Net.Session.LocalId) return current != null && current.Stand;
            if (!remote.TryGetValue(id, out r)) return false;
            foreach (Seat s in seats) if (s.Car == r.Car && s.Index == r.Index) return s.Stand;
            return false;
        }
        public static float RemoteSeatYaw(int id)
        {
            Remote r;
            if (!remote.TryGetValue(id, out r)) return 0f;
            foreach (Seat s in seats) if (s.Car == r.Car && s.Index == r.Index) return s.Yaw;
            return 0f;
        }

        // Avatar d'un autre joueur assis en passager : voiture locale et place de sa tete (repere voiture).
        public static bool RemoteSeat(int id, out Transform car, out Vector3 head, out string carName)
        {
            car = null; head = Vector3.zero; carName = null;
            // (joueur local -- son avatar a la troisieme personne : sa place d'ici)
            if (id == Net.Session.LocalId)
            {
                if (current == null || current.CarT == null) return false;
                car = current.CarT; carName = current.Car; head = current.Head;
                return true;
            }
            Remote rs;
            if (!remote.TryGetValue(id, out rs)) return false;
            // (Voiture inactive ici -- taxi range : l'avatar reste a la place envoyee.)
            foreach (Seat s in seats)
                if (s.Car == rs.Car && s.CarT != null && s.CarT.gameObject.activeInHierarchy)
                {
                    car = s.CarT; carName = s.Car;
                    head = rs.Head != Vector3.zero ? rs.Head : SeatHead(rs.Car, rs.Index);
                    return true;
                }
            return false;
        }

        // Place d'un autre joueur assis (rang ; -1 : pas assis).
        public static int RemoteSeatIndex(int id) { if (id == Net.Session.LocalId) return current != null ? current.Index : -1; Remote rs; return remote.TryGetValue(id, out rs) ? rs.Index : -1; }

        static Vector3 SeatHead(string car, int index)
        {
            foreach (Seat s in seats) if (s.Car == car && s.Index == index) return s.Head;
            return Vector3.zero;
        }

        // Essais : assoit le joueur local a la place 'index' de 'car' (debout dans l'habitacle, puis ENTREE).
        // Essais : autour de chaque place de la voiture 'car' : surface sous la tete (distance, objet), parois devant,
        // derriere, a gauche, a droite (rayons depuis la tete).
        public static string ProbeSeats(string car)
        {
            if (seats.Count == 0) Scan();
            var sb = new System.Text.StringBuilder();
            foreach (Seat s in seats)
            {
                if (!s.Car.StartsWith(car) || s.CarT == null) continue;
                Vector3 h = s.CarT.TransformPoint(s.Head);
                sb.Append(" | place ").Append(s.Index).Append(' ').Append(s.Head.ToString("F2")).Append(" :");
                Vector3[] dirs = { -s.CarT.up, s.CarT.up, s.CarT.forward, -s.CarT.forward, -s.CarT.right, s.CarT.right };
                string[] nm = { "bas", "haut", "avant", "arriere", "gauche", "droite" };
                for (int k = 0; k < dirs.Length; k++)
                {
                    RaycastHit hit = new RaycastHit(); bool any = false;
                    foreach (RaycastHit x in Physics.RaycastAll(h, dirs[k], 3f)) if (!x.collider.isTrigger && Game.RootName(x.collider.transform) != "PLAYER" && (!any || x.distance < hit.distance)) { hit = x; any = true; }
                    if (any) sb.Append(' ').Append(nm[k]).Append(' ').Append(hit.distance.ToString("F2")).Append(" (").Append(hit.collider.name).Append(')');
                    else sb.Append(' ').Append(nm[k]).Append(" -");
                }
            }
            // Ancre du joueur (PLAYER) par rapport a ses pieds, et sol a cote de la cabine (repere de la voiture) : la zone d'une
            // place doit exclure un joueur debout par terre (retour de JD, 08/10 : assis sous la cabine de la GIFU).
            // Points types (ancre du joueur, repere de la voiture) : places dont la zone les accepte.
            Vector3[] pts = { new Vector3(-1.4f, 0.10f, 2.19f), new Vector3(-1.2f, 0.9f, 2.4f), new Vector3(-0.5f, 1.35f, 2.4f), new Vector3(0.7f, 1.35f, 2.9f) };
            string[] pn = { "par terre a gauche", "marchepied gauche", "plancher de la cabine", "debout a la place avant" };
            for (int i = 0; i < pts.Length; i++)
            {
                sb.Append(" | ").Append(pn[i]).Append(" :");
                bool any = false;
                foreach (Seat s in seats) if (s.Car.StartsWith(car) && InSeatZone(s, pts[i])) { sb.Append(" place ").Append(s.Index); any = true; }
                if (!any) sb.Append(" aucune");
            }
            GameObject pl = Game.PlayerGo;
            CharacterController cc = pl != null ? pl.GetComponent<CharacterController>() : null;
            if (cc != null) sb.Append(" | joueur : pieds a ").Append((cc.center.y - cc.height / 2f).ToString("F2")).Append(" m de son ancre");
            foreach (Seat s in seats)
            {
                if (!s.Car.StartsWith(car) || s.CarT == null) continue;
                foreach (float sx in new[] { -1.7f, 1.7f })
                {
                    Vector3 o = s.CarT.TransformPoint(new Vector3(sx, 3.5f, s.Head.z));
                    float best = float.MaxValue;
                    foreach (RaycastHit x in Physics.RaycastAll(o, -s.CarT.up, 8f)) if (!x.collider.isTrigger && x.collider.transform.root != s.CarT.root && x.distance < best) best = x.distance;
                    sb.Append(" | sol a x ").Append(sx.ToString("F1")).Append(" z ").Append(s.Head.z.ToString("F2")).Append(" : ").Append(best < float.MaxValue ? (3.5f - best).ToString("F2") : "?");
                }
                break;
            }
            return sb.ToString();
        }

        // Essais : coupe de la cabine (repere de la voiture) : pour x et z donnes, toutes les surfaces traversees par un rayon
        // vertical descendant depuis 3 m (hauteur locale, objet).
        public static string ProbeGrid(string car)
        {
            if (seats.Count == 0) Scan();
            Transform ct = null;
            foreach (Seat s in seats) if (s.Car.StartsWith(car) && s.CarT != null) { ct = s.CarT; break; }
            if (ct == null) return "voiture absente";
            var sb = new System.Text.StringBuilder();
            for (float z = 3.2f; z >= 1.2f; z -= 0.2f)
                for (float x = -0.5f; x <= 0.51f; x += 0.5f)
                {
                    Vector3 o = ct.TransformPoint(new Vector3(x, 3.5f, z));
                    var hits = Physics.RaycastAll(o, -ct.up, 4f);
                    System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                    sb.Append(" | x ").Append(x.ToString("F1")).Append(" z ").Append(z.ToString("F1")).Append(" :");
                    foreach (RaycastHit h in hits) if (!h.collider.isTrigger && h.collider.transform.root == ct) sb.Append(' ').Append(ct.InverseTransformPoint(h.point).y.ToString("F2")).Append('(').Append(h.collider.name).Append(')');
                }
            return sb.ToString();
        }

        public static string TestSit(string car, int index)
        {
            if (!FindPlayer()) return "pas de joueur";
            if (seats.Count == 0) Scan();
            foreach (Seat s in seats)
                if (s.Car.StartsWith(car) && s.Index == index && s.CarT != null && s.CarT.gameObject.activeInHierarchy)
                {
                    StandIn(s);
                    Sit(s);
                    return "assis dans " + s.Car + " place " + index;
                }
            string l = "";
            foreach (Seat s in seats) if (s.Car.StartsWith(car)) l += " " + s.Index + (s.CarT == null ? ":null" : ":" + Recon.Path(s.CarT) + (s.CarT.gameObject.activeInHierarchy ? "" : "(inactive)"));
            return "aucune place " + index + " sur " + car + " (" + seats.Count + ") :" + l;
        }

        // Essais : comme ENTREE la ou se tient le joueur (zone d'une place).
        public static string TestEnter()
        {
            if (!FindPlayer()) return "pas de joueur";
            Seat z = InZone();
            if (z == null) return "hors zone : " + PlaceDans("");
            Sit(z);
            return "assis place " + z.Index;
        }

        static void StandIn(Seat s)
        {
            if (controller != null) controller.enabled = false;
            player.position = s.CarT.TransformPoint(s.Head + new Vector3(0f, -0.55f, -0.1f));
            if (controller != null) controller.enabled = true;
        }

        // Essais : met le joueur debout dans l'habitacle, a la place 'index' ; dit si la zone le voit.
        public static string TestStandIn(string car, int index)
        {
            if (!FindPlayer()) return "pas de joueur";
            if (seats.Count == 0) Scan();
            foreach (Seat s in seats)
                if (s.Car.StartsWith(car) && s.Index == index && s.CarT != null) { StandIn(s); return "debout dans " + s.Car + " place " + index + " : " + PlaceDans(car); }
            return "aucune place";
        }

        public static string PlaceDans(string car)
        {
            if (!FindPlayer()) return "?";
            foreach (Seat s in seats)
                if ((car.Length == 0 || s.Car.StartsWith(car)) && s.CarT != null && (car.Length > 0 || (s.CarT.position - player.position).sqrMagnitude < 25f))
                {
                    Seat z = current == null ? InZone() : null;
                    return "joueur " + s.CarT.InverseTransformPoint(player.position).ToString("F2") + ", camera " + s.CarT.InverseTransformPoint(cam.position).ToString("F2")
                           + (current != null ? ", assis place " + current.Index : z != null ? ", zone place " + z.Index : ", hors zone");
                }
            return "?";
        }

        // Essais : camera du joueur local et DriverHeadPivot dans le repere de la voiture 'car'.
        public static string DriverEyes(string car)
        {
            if (!FindPlayer()) return "pas de joueur";
            for (int ci = 0; ci < VehicleSync.LocalCount; ci++)
            {
                Rigidbody rb = VehicleSync.LocalBody(ci);
                if (rb == null || !rb.name.StartsWith(car)) continue;
                Transform dhp = Find(rb.transform, "DriverHeadPivot");
                return car + " : camera " + rb.transform.InverseTransformPoint(cam.position).ToString("F3")
                       + ", DriverHeadPivot " + (dhp != null ? rb.transform.InverseTransformPoint(dhp.position).ToString("F3") : "?");
            }
            return "pas de " + car;
        }

        public static string TestLeave() { if (current == null) return "pas assis"; Leave(); return "sorti"; }

        // Essais : attache / detache la ceinture du passager (comme un clic sur la boucle).
        // Essais : ce que voit la ceinture du passager avant depuis la camera (repere de la voiture) : camera, ceinture rangee
        // (centre et taille de ses rendus), boucle, distances et angles au regard, et ce que touche un rayon vers la ceinture.
        public static string TestBeltAim()
        {
            if (current == null || cam == null) return "pas assis";
            Belt b = current.Index == 0 ? BeltOf(current.CarT) : null;
            if (b == null) return "pas de ceinture ici";
            Transform car = current.CarT;
            var sb = new System.Text.StringBuilder("ceinture passager : camera " + car.InverseTransformPoint(cam.position).ToString("F2"));
            Vector3 bp = car.TransformPoint(b.Buckle);
            sb.Append(", boucle ").Append(b.Buckle.ToString("F2")).Append(" a ").Append((bp - cam.position).magnitude.ToString("F2")).Append(" m ")
              .Append(Vector3.Angle(cam.forward, bp - cam.position).ToString("F0")).Append(" deg");
            Renderer or = b.Open != null ? b.Open.GetComponentInChildren<Renderer>() : null;
            if (or == null) sb.Append(", ceinture rangee : aucun rendu (" + (b.Open != null ? b.Open.name : "-") + ")");
            else
            {
                Vector3 c = or.bounds.center;
                sb.Append(", ceinture rangee ").Append(or.name).Append(" centre ").Append(car.InverseTransformPoint(c).ToString("F2")).Append(" taille ").Append(or.bounds.size.ToString("F2"))
                  .Append(" a ").Append((c - cam.position).magnitude.ToString("F2")).Append(" m ").Append(Vector3.Angle(cam.forward, c - cam.position).ToString("F0")).Append(" deg");
                foreach (RaycastHit h in Physics.RaycastAll(cam.position, (c - cam.position).normalized, 2f))
                    sb.Append(" | rayon : ").Append(Recon.Path(h.collider.transform)).Append(" a ").Append(h.distance.ToString("F2")).Append(h.collider.isTrigger ? " (declencheur)" : "");
            }
            return sb.ToString();
        }

        public static string TestBeltCopy()
        {
            if (current == null) return "pas assis";
            Belt b = BeltOf(current.CarT);
            if (b == null || b.Copy == null) return "pas de copie";
            Renderer r = b.Copy.GetComponent<Renderer>();
            MeshFilter mf = b.Copy.GetComponent<MeshFilter>();
            return "copie " + (b.CopyPlain ? "telle quelle" : "bombee") + ", active " + b.Copy.activeInHierarchy + ", calque " + b.Copy.layer + ", rendu " + (r != null && r.enabled) + ", maillage " + (mf != null && mf.sharedMesh != null ? mf.sharedMesh.name + " " + mf.sharedMesh.vertexCount : "?")
                   + ", centre " + current.CarT.InverseTransformPoint(r.bounds.center).ToString("F2") + " taille " + r.bounds.size.ToString("F2") + ", tete " + current.Head.ToString("F2") + ", camera " + (cam != null ? current.CarT.InverseTransformPoint(cam.position).ToString("F2") : "?");
        }

        // Essais : point vise pour regarder la ceinture rangee (0), la boucle (1) ou le siege (2, comme en baissant les yeux).
        public static bool TestBeltTarget(int what, out Vector3 target)
        {
            target = Vector3.zero;
            if (current == null || current.Index != 0) return false;
            Belt b = BeltOf(current.CarT);
            if (b == null) return false;
            Renderer or = b.Open != null ? b.Open.GetComponentInChildren<Renderer>() : null;
            target = what == 0 && or != null ? or.bounds.center : what == 2 ? current.CarT.TransformPoint(current.Head + new Vector3(0f, -0.75f, 0.12f)) : current.CarT.TransformPoint(b.Buckle);
            return true;
        }

        // Essais : tourne le regard (camera de la tete) vers la ceinture rangee du passager (0) ou vers la boucle (1).
        public static string TestBeltLook(int what)
        {
            if (current == null || cam == null) return "pas assis";
            Belt b = current.Index == 0 ? BeltOf(current.CarT) : null;
            if (b == null) return "pas de ceinture";
            Renderer or = b.Open != null ? b.Open.GetComponentInChildren<Renderer>() : null;
            Vector3 target = what == 0 && or != null ? or.bounds.center : current.CarT.TransformPoint(b.Buckle);
            cam.rotation = Quaternion.LookRotation(target - cam.position);
            return "regard vers " + (what == 0 ? "la ceinture rangee" : "la boucle");
        }

        // Essais : la ceinture tenue (comme un clic maintenu sur la ceinture rangee) ; hold=false : lachee.
        public static string TestPull(bool hold)
        {
            if (current == null || current.Index != 0) return "pas assis a l'avant";
            Belt b = BeltOf(current.CarT);
            testHold = hold;
            if (hold)
            {
                if (beltHold <= 0f) Grab(b);
                return "ceinture tenue" + (heldHandle != null ? " : languette " + Recon.Path(heldHandle) + " en " + heldHandle.position.ToString("F2") : " : pas de languette");
            }
            return "ceinture lachee";   // (BeltInput la lache a l'image suivante : bouclee si on vise la boucle)
        }

        public static string TestBelt()
        {
            if (current == null) return "pas assis";
            if (current.Index > 0)
            {
                Belt rb = BeltOf(current.CarT); Vector3 off; bool mir;
                if (rb == null || !RearPlace(current.CarT, current.Index, out off, out mir)) return "pas de ceinture a cette place arriere";
                beltOn = !beltOn;
                SendSeat(current.Car, current.Index, headLocal);
                UpdateBelts();
                GameObject g; rb.Rear.TryGetValue(current.Index, out g);
                RearModel rm; rearModels.TryGetValue(current.CarT, out rm);
                string model = HasRearModel(current.CarT) ? (rm != null && rm.Root != null ? "Menos, " + (rm.Root.activeSelf ? "montre" : "cache") + ", etat " + rm.State : "Menos, absent") : g != null ? "montre" : "absent";
                if (rm != null && rm.Root != null)
                    foreach (MeshRenderer mr in rm.Root.GetComponentsInChildren<MeshRenderer>(true))
                        model += " ; " + mr.name + (mr.gameObject.activeInHierarchy ? "" : " (eteint)") + " centre " + current.CarT.InverseTransformPoint(mr.bounds.center).ToString("F2") + " taille " + mr.bounds.size.ToString("F2") + " couche " + mr.gameObject.layer;
                if (rb.Fastened != null)
                {
                    Transform ft = rb.Fastened.transform; MeshFilter mf = ft.GetComponent<MeshFilter>();
                    var chain = new System.Text.StringBuilder();
                    for (Transform x = ft; x != null && x != current.CarT; x = x.parent) chain.Append(" <- ").Append(x.name).Append(' ').Append(x.localPosition.ToString("F3")).Append(' ').Append(x.localEulerAngles.ToString("F0")).Append(' ').Append(x.localScale.ToString("F2"));
                    MeshRenderer fr = ft.GetComponent<MeshRenderer>();
                    if (fr != null) model += " ; AVANT rendu centre " + current.CarT.InverseTransformPoint(fr.bounds.center).ToString("F3") + " taille " + fr.bounds.size.ToString("F3");
                    model += " ; AVANT " + Recon.Path(ft) + " : voiture " + current.CarT.InverseTransformPoint(ft.position).ToString("F3") + " rot " + (Quaternion.Inverse(current.CarT.rotation) * ft.rotation).eulerAngles.ToString("F0")
                           + ", maillage local " + (mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds.min.ToString("F2") + " .. " + mf.sharedMesh.bounds.max.ToString("F2") : "?") + ", chaine" + chain;
                }
                return "ceinture arriere " + (beltOn ? "attachee" : "detachee") + " (place " + current.Index + ", tete x " + current.Head.x.ToString("F2") + ", decalage " + off.ToString("F2") + (mir ? ", miroir" : "") + ", boucle " + RearBuckle(rb, off, mir).ToString("F2") + ", modele " + model + ")";
            }
            Belt b = current.Index == 0 ? BeltOf(current.CarT) : null;
            if (b == null) return "pas de ceinture ici";
            beltOn = !beltOn;
            SendSeat(current.Car, current.Index, headLocal);
            UpdateBelts();
            string where = "";
            if (b.Copy != null)
            {
                Renderer cr = b.Copy.GetComponent<Renderer>(), fr = b.Fastened.GetComponent<Renderer>();
                Transform car = current.CarT;
                where = ", copie centre " + car.InverseTransformPoint(cr.bounds.center).ToString("F2") + " taille " + cr.bounds.size.ToString("F2")
                        + " ; modele centre " + car.InverseTransformPoint(fr.bounds.center).ToString("F2") + " actif " + b.Fastened.activeInHierarchy
                        + " ; tete " + current.Head.ToString("F2") + " ; echelle " + b.Copy.transform.localScale.ToString("F2");
            }
            return "ceinture " + (beltOn ? "attachee" : "detachee") + ", copie " + (b.Copy != null) + ", rangee cachee " + b.OpenHidden + where;
        }

        // Essais : copies de ceinture montrees ici (passager, conducteur) et sangle du conducteur cachee.
        public static string BeltState()
        {
            var sb = new System.Text.StringBuilder();
            foreach (KeyValuePair<int, bool> kv in driverBelt) sb.Append("#").Append(kv.Key).Append(kv.Value ? " boucle" : " detache").Append(", ");
            foreach (KeyValuePair<Transform, Belt> kv in belts)
                if (kv.Key != null && kv.Value != null)
                    sb.Append(kv.Key.name).Append(" : passager ").Append(kv.Value.Copy != null).Append(", conducteur ").Append(kv.Value.DriverCopy != null)
                      .Append(" (sangle cachee ").Append(kv.Value.DriverOpenHidden).Append(", copie a ").Append(kv.Value.DriverCopy != null ? kv.Key.InverseTransformPoint(kv.Value.DriverCopy.GetComponent<Renderer>().bounds.center).ToString("F2") : "-").Append("), ");
            return sb.Length > 0 ? sb.ToString() : "aucune";
        }

        // Essais ([Test] Autotest=detacher) : ceinture du conducteur de 'car' par les automates du jeu. action 1 : boucler
        // (SeatbeltHandle 'Use' -> State 2), 2 : detacher (SeatbeltLock 'Use' -> State 2), 0 : etat seulement -- boucle
        // visible, etat de son automate, et ce que croise le regard du joueur vers elle (ce qui prendrait le clic).
        public static string TestDriverBelt(string car, int action)
        {
            Rigidbody b = VehicleSync.Body(car);
            if (b == null) return "voiture " + car + " introuvable";
            Transform lk = FindUnder(b.transform, "SeatbeltLock", null), hd = FindUnder(b.transform, "SeatbeltHandle", null);
            PlayMakerFSM lu = lk != null ? Game.FsmOn(lk.gameObject, "Use") : null, hu = hd != null ? Game.FsmOn(hd.gameObject, "Use") : null;
            if (action == 1 && hu != null) { if (lk != null) lk.gameObject.SetActive(true); Game.SetState(hu, "State 2"); }   // (comme 'Wait lock' : boucle montree en tirant)
            if (action == 2 && lu != null) Game.SetState(lu, "State 2");
            var sb = new System.Text.StringBuilder("ceinture conducteur " + car + (action == 1 ? " (boucler)" : action == 2 ? " (detacher)" : "") + " : PlayerSeatbeltsOn " + Game.GlobalBool("PlayerSeatbeltsOn"));
            if (lk != null)
            {
                Collider c = lk.GetComponent<Collider>();
                sb.Append(", boucle ").Append(lk.gameObject.activeInHierarchy ? "visible" : "cachee").Append(" (").Append(lu != null ? lu.ActiveStateName : "?").Append(", collisionneur ").Append(c != null && c.enabled).Append(')');
                Transform cm = PlayerSync.LocalCamera;
                if (cm != null && c != null)
                {
                    Vector3 d = c.bounds.center - cm.position;
                    RaycastHit[] hits = Physics.RaycastAll(cm.position, d.normalized, d.magnitude + 0.1f);
                    System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
                    sb.Append(", regard ").Append(d.magnitude.ToString("F2")).Append(" m :");
                    foreach (RaycastHit h in hits) sb.Append(' ').Append(Recon.Path(h.collider.transform)).Append(h.collider.isTrigger ? "(decl)" : "").Append('@').Append(h.distance.ToString("F2"));
                    // (le clic du jeu : MousePickEvent, couche de la boucle seulement, 1 m)
                    RaycastHit lh;
                    sb.Append(", couche ").Append(lk.gameObject.layer).Append(" : ").Append(Physics.Raycast(cm.position, d.normalized, out lh, 1f, 1 << lk.gameObject.layer) ? Recon.Path(lh.collider.transform) + "@" + lh.distance.ToString("F2") : "rien a 1 m");
                }
            }
            else sb.Append(", pas de SeatbeltLock");
            if (hd != null) sb.Append(", poignee ").Append(hd.gameObject.activeInHierarchy ? "visible" : "cachee").Append(" (").Append(hu != null ? hu.ActiveStateName : "?").Append(')');
            foreach (Transform x in new[] { lk, hd })
                if (x != null && !x.gameObject.activeInHierarchy)
                    for (Transform p = x; p != null && p != b.transform; p = p.parent)
                        if (!p.gameObject.activeSelf) { sb.Append(", ").Append(x.name).Append(" eteint par ").Append(VehicleSync.RelPath(b.transform, p)); break; }
            sb.Append(" | ").Append(CarVisuals.State(car, "SeatbeltLock")).Append(" | ").Append(CarVisuals.State(car, "SeatbeltHandle"));
            return sb.ToString();
        }

        // Essais : places passagers de la voiture de cle 'car'.
        public static int CountFor(string car)
        {
            int n = 0;
            foreach (Seat s in seats) if (s.Car == car) n++;
            return n;
        }
    }
}
