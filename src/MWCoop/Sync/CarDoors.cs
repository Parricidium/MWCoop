using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Portieres, coffres, hayons et capots des vehicules, et prises du chauffage moteur. Automate 'Use' :
    //  - portieres : clic -> "Open door" (ouvre tant que le bouton est tenu), clic sur une portiere ouverte ->
    //    "Open door 2" (la pousse vers la fermeture tant que le bouton est tenu ; relachee avant, elle reste
    //    ouverte), arrivee fermee -> "Sound" -> "Close door" (claque, verrouillee). Refermee en la poussant
    //    pendant l'ouverture : "Reset 2" (verrou) ;
    //  - hayons, capots : "Open hood", "State 2" (pousse vers la fermeture), "Sound" -> "Close hood".
    // Evenements (actions ajoutees en tete des etats) : 1 ouverte, 3 saisie (pousse vers la fermeture),
    // 0 fermee pour de bon (Sound : pas au clic, qui peut etre relache avant), 4 verrouillee.
    // L'hote est l'arbitre : il applique les evenements dans l'ordre d'arrivee et les renvoie a TOUS, auteur
    // compris. Chacun les rejoue dans cet ordre (un invite saute ceux d'autrui tant qu'il attend le retour
    // des siens : ils sont ordonnes avant les siens). Deux clics croises finissent donc pareil partout.
    // Rejouer une fermeture : ramenee a 0 (capot : "State 2" leve d'abord ses butees d'ouverture), puis "Sound"
    // -> "Close door" (claque, verrou) : la souris de celui qui la rejoue n'y peut rien (avant : "Open door 2",
    // que son clic arretait).
    // Le verrou ("Reset 2" -> "Set lock 2" : une attache rigide a la carrosserie, qui casse quand on tire)
    // n'est PAS rejoue : chez les autres la portiere reste figee a l'angle de celui qui l'a poussee (une
    // attache jamais cassee chez eux tirerait la voiture a la fermeture suivante). Une attache de verrou posee
    // chez nous sur une portiere qu'un autre manie (ouverture rejouee, angle lu 359 degres a l'ouverture) est
    // retiree : sur la copie d'une voiture conduite ailleurs, VehicleSync rend les attaches incassables, et
    // celle-ci soudait la portiere (ouverte chez le conducteur, fermee ici). Pareil pour le verrou que le jeu
    // pose en ouvrant (moins de 0,7 s apres l'ouverture : jamais annonce aux autres). Le notre, garde, reste
    // cassable sur la copie (IsDoorLock : ProtectJoints le saute, lui seul).
    // Les portieres gauches n'ont pas de SetRotation dans "Close door" : une fermeture rejouee est d'abord
    // posee sur la pose fermee (pivotee autour de la charniere), PUIS le jeu change ses butees -- sinon la
    // charniere est recreee avec la pose entrouverte pour zero et sa course derive a chaque cycle.
    // Position en temps reel : celui qui l'a ouverte ou saisie en dernier (Owner) envoie l'angle de sa
    // charniere, mesure depuis sa pose fermee (le repere des butees et du ressort : 0 fermee ; HingeJoint.angle,
    // lui, lit 120 coffre ferme et 93 ouvert pour des butees 0 et 60, inutilisable). Chez les autres, la portiere reste un corps physique ordinaire, menee a cet
    // angle par une petite correction de sa vitesse autour de l'axe de la charniere (a chaque image ; la charniere
    // elle-meme n'est JAMAIS modifiee : dans ce Unity, changer son ressort ou ses butees la recree avec la pose
    // du moment pour zero -- portieres « fermees » a 120 degres apres un spam de clics), et ne heurte plus le joueur local (elle traverse les joueurs comme
    // chez celui qui la manie : les avatars n'ont pas de collision). Jamais cinematique ni teleportee : une
    // portiere cinematique (masse infinie) accrochee a une voiture qui roule la tirait -- coffre referme pendant
    // qu'un autre conduisait : voiture envolee (retour de JD, 0.16).
    // Voitures : celles de VehicleSync (taxi JOBS/TAXIJOB/MACHTWAGEN compris), cle = cle de la voiture + chemin sous
    // elle ; releve toutes les 30 s et des que sa liste change (voiture activee plus tard).
    public static class CarDoors
    {
        const int K_CLOSED = 0, K_OPEN = 1, K_ANGLE = 2, K_GRAB = 3, K_LOCK = 4;
        const int K_LOCKSET = 5;   // (local : fin de "Set lock 2", l'attache du verrou vient d'etre posee ; jamais envoye)
        enum DoorState { Closed, Open, Locked }

        class Door
        {
            public string Key; public PlayMakerFSM Fsm; public string Open, Grab, Close;
            public bool HasLock, IsDoor;
            public string LockState, SetLockState;     // "Reset 2" -> "Set lock 2" (camion : "Reset 3" -> "Set lock 3")
            public bool NoRot;                         // fermeture sans SetRotation (portieres gauches) : posee par nous
            public Rigidbody Body; public HingeJoint Hinge;
            public DoorState State;
            public int Owner = -1; public bool Mine;   // dernier a l'avoir maniee ; nous (on envoie son angle)
            public int PendingOwn;                     // invite : evenements envoyes dont le retour de l'hote manque
            public float LastAngle; public bool Sent;
            public bool Showing; public float TargetAngle; public float LastRemote;
            public bool Springing;                     // menee vers TargetAngle (Follow)
            public float NextCheck;                    // suivie : paires joueur/portiere et verrous revus (0,5 s)
            public Collider[] Colls;                   // collisionneurs de la portiere
            public readonly Dictionary<long, KeyValuePair<Collider, Collider>> Ignored = new Dictionary<long, KeyValuePair<Collider, Collider>>();   // paires (portiere, joueur local) ignorees par nous
            public bool Closing; public float ClosingSince;   // fermeture rejouee : ramenee a 0 avant de claquer
            public float LastSentAt, OpenedAt;
            public Joint[] PreLock;                    // attaches presentes a l'entree du verrou
            public readonly List<Joint> Locks = new List<Joint>();   // attaches posees par "Set lock 2"
            public bool LockSpurious;                  // verrou d'ouverture (359 degres) ou rejoue : retire une fois pose
            public float LockCheckUntil;               // ouverture rejouee : l'automate ne doit pas rester au verrou
            public float TraceUntil, NextTrace;        // essais : [Test] TracePortiere
            public bool RestSet; public Quaternion RestRot;   // pose fermee (repere du parent)
            public float TestOffset;   // essais : degres ajoutes a l'angle envoye
            public bool Held;          // fermee sur la copie d'une voiture conduite ailleurs : figee sur la caisse (Hold)
            public FsmBool PlugOn;     // prise du chauffage : 'On' du jeu (branchee), l'etat vrai (l'automate ne reste qu'1 s dans "Heater on")
        }

        static readonly Dictionary<string, Door> byKey = new Dictionary<string, Door>();
        static readonly HashSet<PlayMakerFSM> hooked = new HashSet<PlayMakerFSM>();
        static readonly HashSet<Joint> lockJoints = new HashSet<Joint>();   // attaches de verrou posees ici (toutes portieres)
        static float nextScan = -1, nextAngle, lastUnknownScan = -100;
        static int gen = -1;   // VehicleSync.Generation au dernier releve
        static readonly List<KeyValuePair<float, Peer>> snapshots = new List<KeyValuePair<float, Peer>>();
        static bool applying;
        // Joueur local : ses collisionneurs pleins (controleur, objet tenu, poings...), revus toutes les 0,5 s.
        static Collider[] plColls = new Collider[0];
        static float plCollsAt = -10f;
        static CharacterController plCc;
        static bool ccWasOn;

        class Hook : ModHook
        {
            public override string Module { get { return "portieres"; } }
            public Door D;
            public int Kind;
            public override void OnEnter()
            {
                try
                {
                    if (Kind == K_LOCKSET) LockSet(D);   // (meme rejoue : c'est l'attache posee ici qui compte)
                    else if (applying || Replay.Depth > 0) { if (Kind == K_LOCK) LockEnter(D, true); }
                    else if (D.Closing && Kind == K_CLOSED) EndClosing(D, false);   // (le jeu acheve la fermeture rejouee)
                    else { if (Kind == K_LOCK) LockEnter(D, false); Local(D, Kind); }
                }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        public static bool Tracks(PlayMakerFSM f) { return hooked.Contains(f); }

        // Attache posee par le verrou du jeu ("Set lock 2", reperee en fin d'etat, avant tout passage de
        // VehicleSync.ProtectJoints) : laissee cassable sur la copie -- incassable, elle soudait la portiere et le
        // clic suivant (qui la casse en tirant) ne l'ouvrait plus. Seulement elle : la charniere, le loquet de la
        // portiere fermee (CORRIS : "State 1") et les attaches de montage ("Set joint 2") restent protegees.
        public static bool IsDoorLock(Joint j) { return j != null && lockJoints.Contains(j); }

        public static void OnLevelLoaded()
        {
            byKey.Clear(); hooked.Clear(); snapshots.Clear(); lockJoints.Clear();
            plColls = new Collider[0]; plCollsAt = -10f; plCc = null; ccWasOn = false; gen = -1;
            nextScan = PlayerSync.InGame ? Time.realtimeSinceStartup + 12f : -1;
        }

        static void Scan()
        {
            int before = byKey.Count;
            gen = VehicleSync.Generation;
            for (int ci = 0; ci < VehicleSync.LocalCount; ci++)
            {
                Rigidbody car = VehicleSync.LocalBody(ci);
                if (car == null) continue;
                string carKey = VehicleSync.LocalKey(ci);
                var seen = new Dictionary<string, int>();
                foreach (PlayMakerFSM f in car.GetComponentsInChildren<PlayMakerFSM>(true))
                {
                    if (f.FsmName != "Use") continue;
                    string open = null, grab = null, close = null;
                    bool isDoor = false;
                    if (f.Fsm.GetState("Open door") != null && f.Fsm.GetState("Open door 2") != null && f.Fsm.GetState("Close door") != null)
                    { open = "Open door"; grab = "Open door 2"; close = f.Fsm.GetState("Sound") != null ? "Sound" : "Close door"; isDoor = true; }
                    else if (f.Fsm.GetState("Open hood") != null && f.Fsm.GetState("Close hood") != null)
                    { open = "Open hood"; grab = f.Fsm.GetState("State 2") != null ? "State 2" : null; close = f.Fsm.GetState("Sound") != null ? "Sound" : "Close hood"; }
                    if (open == null) continue;
                    string rel = VehicleSync.RelPath(car.transform, f.transform);
                    int k; seen.TryGetValue(rel, out k); seen[rel] = k + 1;   // compte aussi les portieres deja suivies
                    if (hooked.Contains(f) || !Replay.Claim(f, "portieres")) continue;
                    var d = new Door { Key = carKey + rel + "#" + k, Fsm = f, Open = open, Grab = grab, Close = close, IsDoor = isDoor };
                    d.Body = f.GetComponentInParent<Rigidbody>();
                    if (d.Body != null && d.Body.transform == car.transform) d.Body = null;
                    d.Hinge = d.Body != null ? d.Body.GetComponent<HingeJoint>() : null;
                    d.State = OpenVar(d) ? DoorState.Open : DoorState.Closed;
                    if (!Inject(d, open, K_OPEN) || !Inject(d, close, K_CLOSED)) continue;
                    if (grab != null) Inject(d, grab, K_GRAB);
                    if (isDoor)
                    {
                        // Verrou : l'etat que LOCK atteint depuis "Open door", puis celui qui pose l'attache (FINISHED).
                        d.LockState = Target(f.Fsm.GetState(open), "LOCK");
                        d.SetLockState = d.LockState != null ? Target(f.Fsm.GetState(d.LockState), "FINISHED") : null;
                        d.NoRot = !HasAction(f.Fsm.GetState("Close door"), "SetRotation");
                    }
                    d.HasLock = d.LockState != null && Inject(d, d.LockState, K_LOCK);
                    if (d.HasLock && d.SetLockState != null) Inject(d, d.SetLockState, K_LOCKSET, true);
                    hooked.Add(f);
                    byKey[d.Key] = d;
                    NoteRest(d);
                }
            }
            // Prises du chauffage moteur (cable plug) : "Heater on" = branchee sur la voiture, "Heater off"
            // = debranchee. Partout dans la scene (debranchee, elle pend a son poteau). Deux prises, maison
            // (HOMENEW) et usine (JOBS/FACTORY), visent la MEME prise de voiture (Socket, SORBET) : cle = l'UT
            // de sauvegarde de la prise (HeaterCable1On, HeaterCable2On). Avec Socket pour cle, l'une ecrasait
            // l'autre et la prise de la maison n'etait plus rejouee chez les autres.
            foreach (PlayMakerFSM f in Object.FindObjectsOfType<PlayMakerFSM>())
            {
                if (f.FsmName != "Data" || hooked.Contains(f) || !f.gameObject.name.StartsWith("cable plug")) continue;
                if (f.Fsm.GetState("Heater on") == null || f.Fsm.GetState("Heater off") == null) continue;
                string pk = PlugKey(f);
                if (pk == null || byKey.ContainsKey(pk) || !Replay.Claim(f, "portieres")) continue;
                var d = new Door { Key = pk, Fsm = f, Open = "Heater on", Close = "Heater off", PlugOn = f.FsmVariables.FindFsmBool("On") };
                PlugState(d);
                if (!Inject(d, d.Open, K_OPEN) || !Inject(d, d.Close, K_CLOSED)) continue;
                hooked.Add(f);
                byKey[d.Key] = d;
            }
            if (byKey.Count != before) Log.Info("portieres : " + byKey.Count + " suivies (portes, coffres, hayons, prises)");
        }

        // Cle d'une prise du chauffage moteur : "prise:" + UT de son automate 'Data' (null : pas une prise reconnue).
        public static string PlugKey(PlayMakerFSM data)
        {
            if (data == null) return null;
            FsmString ut = data.FsmVariables.FindFsmString("UT");
            return ut != null && !string.IsNullOrEmpty(ut.Value) ? "prise:" + ut.Value : null;
        }

        // Action ajoutee en tete de l'etat (atEnd : en fin, apres les actions du jeu).
        static bool Inject(Door d, string state, int kind, bool atEnd = false)
        {
            FsmState s = d.Fsm.Fsm.GetState(state);
            if (s == null || !s.IsInitialized) return false;   // (automate jamais demarre : au prochain releve)
            try
            {
                var list = new List<FsmStateAction>(s.Actions);
                list.Insert(atEnd ? list.Count : 0, new Hook { D = d, Kind = kind });
                s.Actions = list.ToArray();
                return true;
            }
            catch { return false; }
        }

        static string Target(FsmState s, string ev)
        {
            if (s == null) return null;
            foreach (FsmTransition t in s.Transitions) if (t.EventName == ev) return t.ToState;
            return null;
        }

        // (Automate jamais demarre : ses actions ne se chargent pas -- le getter de PlayMaker leve une exception.)
        static bool HasAction(FsmState s, string type)
        {
            if (s == null || !s.IsInitialized) return false;
            try { foreach (FsmStateAction a in s.Actions) if (a != null && a.GetType().Name == type) return true; }
            catch { }
            return false;
        }

        static bool OpenVar(Door d)
        {
            FsmBool o = d.Fsm.FsmVariables.FindFsmBool("Open");
            return o != null && o.Value;
        }

        // Pose fermee (repere de la voiture) : portiere fermee pour le jeu (verrouillee par ses butees).
        static void NoteRest(Door d)
        {
            if (d.RestSet || d.Hinge == null || d.Body == null || d.Springing || d.State != DoorState.Closed || OpenVar(d)) return;
            d.RestSet = true; d.RestRot = d.Body.transform.localRotation;
        }

        // Angle (degres) autour de l'axe de la charniere depuis la pose fermee : le repere des butees du jeu.
        static float Angle(Door d)
        {
            if (!d.RestSet) return 0f;
            Quaternion rel = Quaternion.Inverse(d.RestRot) * d.Body.transform.localRotation;
            float a; Vector3 ax;
            rel.ToAngleAxis(out a, out ax);
            if (a > 180f) a -= 360f;
            return Vector3.Dot(ax, d.Hinge.axis) < 0f ? -a : a;
        }

        public static void Update()
        {
            if (!Session.Active || nextScan < 0) return;
            float now = Time.realtimeSinceStartup;
            // Liste des voitures changee apres le premier releve (taxi active, cyclomoteur recree) : releve tout de suite.
            if (now >= nextScan || (gen >= 0 && gen != VehicleSync.Generation)) { nextScan = now + 30f; Scan(); }
            // Arrivee d'un joueur : les portieres, capots et prises deja ouverts/branches chez l'hote.
            for (int i = snapshots.Count - 1; i >= 0; i--)
            {
                if (now < snapshots[i].Key) continue;
                Peer p = snapshots[i].Value;
                snapshots.RemoveAt(i);
                if (!p.Accepted || !Session.T.Peers.Contains(p)) continue;
                int n = 0;
                foreach (Door d in byKey.Values)
                    if (d.State != DoorState.Closed && d.Fsm != null)
                    {
                        // Ouverte sans main (deja ouverte au chargement, joueur parti) : l'hote la prend, sinon
                        // personne n'enverrait son angle au nouveau venu.
                        if (d.Owner < 0) Take(d);
                        Session.T.SendReliable(p, new NetWriter(Msg.CarDoor).U8(d.Owner >= 0 ? d.Owner : Session.LocalId).Str(d.Key).U8(K_OPEN).ToArray());
                        n++;
                    }
                Log.Info("portieres : " + n + " ouvertes envoyees a " + p);
            }
            // Controleur du joueur local coupe ou remis (assis, debout, reapparition) : le jeu a oublie ses paires
            // ignorees -- reposees tout de suite sur les portieres suivies.
            bool ccOn = plCc != null && plCc.enabled;
            if (ccOn != ccWasOn)
            {
                ccWasOn = ccOn;
                plCollsAt = -10f;
                foreach (Door d in byKey.Values) d.NextCheck = 0f;
            }
            // Chez nous, pour un autre : le ressort de la charniere la mene a son angle ; fermeture rejouee :
            // ramenee a 0, puis le jeu la claque (comme quand on la pousse jusqu'au bout).
            bool trace = Config.GetInt("Test", "TracePortiere", 0) != 0;
            foreach (Door d in byKey.Values)
            {
                Hold(d);
                NoteRest(d);
                if (trace && now < d.TraceUntil && now >= d.NextTrace && d.Hinge != null)
                { d.NextTrace = now + 0.1f; Log.Info("trace " + d.Key + " " + Angle(d).ToString("F1") + (d.Springing ? " -> " + d.TargetAngle.ToString("F1") : "") + " " + Diag(d)); }
                if (d.LockCheckUntil > 0f) CheckLock(d, now);
                if (d.Closing)
                {
                    // Copie d'une voiture conduite ailleurs : la portiere suit la caisse par a-coups, plus de temps.
                    float limit = now - d.ClosingSince > 1.2f && d.Fsm != null && VehicleSync.RemotelyDriven(d.Fsm.transform) ? 3f : 1.2f;
                    if (d.Hinge == null || !d.RestSet || Mathf.Abs(Angle(d)) < 1.5f || now - d.ClosingSince > limit) EndClosing(d, true);
                    else { d.TargetAngle = 0f; Follow(d); }
                    continue;
                }
                if (!d.Showing) continue;
                if (d.Body == null || d.Hinge == null || !d.RestSet || d.State == DoorState.Closed || d.Mine) { StopFollow(d); continue; }
                Follow(d);
            }
            if (now < nextAngle || Session.RemoteCount == 0) return;
            nextAngle = now + 1f / 15f;
            foreach (Door d in byKey.Values)
            {
                if (!d.Mine || d.State == DoorState.Closed || d.Body == null || d.Fsm == null || d.Hinge == null || !d.RestSet) continue;
                float ang = Angle(d) + d.TestOffset;
                if (d.Sent && Mathf.Abs(Mathf.DeltaAngle(ang, d.LastAngle)) < 0.4f && now - d.LastSentAt < 1f) continue;
                d.Sent = true;
                d.LastAngle = ang;
                d.LastSentAt = now;
                Session.SendAll(new NetWriter(Msg.CarDoor).U8(Session.LocalId).Str(d.Key).U8(K_ANGLE).F32(ang), false);
            }
        }

        public static void ScheduleSnapshot(Peer p) { if (Session.IsHost) snapshots.Add(new KeyValuePair<float, Peer>(Time.realtimeSinceStartup + 20f, p)); }

        // Joueur parti : ses portieres ouvertes ne suivent plus personne ; l'hote les reprend.
        public static void PlayerLeft(int id)
        {
            foreach (Door d in byKey.Values)
            {
                if (d.Owner != id) continue;
                d.Owner = -1;
                StopFollow(d);
                if (Session.IsHost && d.State != DoorState.Closed && d.Fsm != null) Local(d, K_GRAB);
            }
        }

        // Copie d'une voiture conduite par un autre (cinematique, deplacee a chaque pas de physique) : une portiere ou un
        // hayon FERME restait un corps libre sur sa charniere (le verrou n'est pas rejoue ici) et ballottait -- 5 mm et
        // 0,3 degre par image pour le hayon de la SORBET, « coffre et banquette qui tremblent » chez le passager (05/10).
        // Fermee et copie : posee fermee et rendue cinematique (enfant de la caisse, elle la suit exactement ; une
        // charniere entre deux corps cinematiques ne tire rien). Rendue des qu'elle s'ouvre ou que la voiture redevient
        // locale (sinon un corps cinematique accroche a une voiture qui roule la tirerait).
        static void Hold(Door d)
        {
            if (d.Body == null || d.Hinge == null) { d.Held = false; return; }
            bool hold = d.RestSet && d.State != DoorState.Open && !d.Closing && !d.Mine && d.Fsm != null && VehicleSync.RemotelyDriven(d.Fsm.transform);
            if (hold == d.Held) return;
            if (hold)
            {
                if (d.Body.isKinematic) return;   // (deja figee par le jeu : rien a faire)
                Snap(d);
                d.Body.isKinematic = true;
                d.Held = true;
                if (holdLogs++ < 20) Log.Info("portieres : " + d.Key + " fermee, figee sur la copie de la voiture");
            }
            else
            {
                d.Held = false;
                d.Body.isKinematic = false;
                d.Body.WakeUp();
            }
        }

        static int holdLogs;

        // Menee vers TargetAngle : sa vitesse autour de l'axe de la charniere (par rapport a la voiture) est
        // corrigee a chaque image, proportionnelle a l'ecart et bornee ; le joueur local ne la bloque pas.
        const float ServoGain = 12f, ServoMax = 360f;   // (1/s, degres/s)
        static void Follow(Door d)
        {
            if (d.Hinge == null || d.Body == null || !d.RestSet || d.Body.isKinematic) return;
            if (!d.Springing) { d.Springing = true; d.NextCheck = 0f; }
            float now = Time.realtimeSinceStartup;
            if (now >= d.NextCheck)
            {
                // Toutes les 0,5 s : le joueur local (et ce qu'il tient) la traverse toujours -- le jeu oublie une
                // paire quand l'un des deux est coupe puis remis (objet pris, assis, penche au volant) ; un verrou
                // pose ici sur la portiere d'un autre est retire.
                d.NextCheck = now + 0.5f;
                IgnorePlayer(d);
                if (!d.Mine && d.Locks.Count > 0) DropLocks(d, "suivie");
            }
            Vector3 axis = d.Body.transform.TransformDirection(d.Hinge.axis).normalized;
            Rigidbody car = d.Hinge.connectedBody;
            Vector3 rel = d.Body.angularVelocity - (car != null ? car.angularVelocity : Vector3.zero);
            float w = Vector3.Dot(rel, axis) * Mathf.Rad2Deg;
            float want = Mathf.Clamp(Mathf.DeltaAngle(Angle(d), d.TargetAngle) * ServoGain, -ServoMax, ServoMax);
            d.Body.AddTorque(axis * ((want - w) * Mathf.Deg2Rad), ForceMode.VelocityChange);
            if (d.Body.IsSleeping()) d.Body.WakeUp();
        }

        // Le joueur local traverse la portiere : chaque collisionneur plein de la portiere ignore chaque
        // collisionneur plein sous PLAYER (controleur, objet tenu sous ItemPivot, poings...). Rappele toutes les
        // 0,5 s (paires deja posees : sans effet) ; on retient celles qu'on a posees pour ne lever qu'elles.
        static void IgnorePlayer(Door d)
        {
            Collider[] pc = PlayerColliders();
            if (pc.Length == 0 || d.Body == null) return;
            if (d.Colls == null) d.Colls = d.Body.GetComponentsInChildren<Collider>(true);
            foreach (Collider c in d.Colls)
            {
                if (!Solid(c)) continue;
                foreach (Collider p in pc)
                {
                    if (!Solid(p)) continue;
                    Physics.IgnoreCollision(c, p, true);
                    long k = ((long)c.GetInstanceID() << 32) | (uint)p.GetInstanceID();
                    if (!d.Ignored.ContainsKey(k)) d.Ignored[k] = new KeyValuePair<Collider, Collider>(c, p);
                }
            }
        }

        // (Unity refuse IgnoreCollision sur un collisionneur coupe ou inactif -- et oublie alors ses paires.)
        static bool Solid(Collider c) { return c != null && !c.isTrigger && c.enabled && c.gameObject.activeInHierarchy; }

        static Collider[] PlayerColliders()
        {
            float now = Time.realtimeSinceStartup;
            if (now - plCollsAt < 0.5f) return plColls;
            plCollsAt = now;
            GameObject pl = GameObject.Find("PLAYER");
            plCc = pl != null ? pl.GetComponent<CharacterController>() : null;
            plColls = pl != null ? pl.GetComponentsInChildren<Collider>() : new Collider[0];
            return plColls;
        }

        static void ClearIgnore(Door d)
        {
            foreach (KeyValuePair<Collider, Collider> kv in d.Ignored.Values)
                if (Solid(kv.Key) && Solid(kv.Value)) Physics.IgnoreCollision(kv.Key, kv.Value, false);
            d.Ignored.Clear();
        }

        // Fin du suivi : le joueur local la heurte de nouveau.
        static void StopFollow(Door d)
        {
            if (d.Springing || d.Ignored.Count > 0) ClearIgnore(d);
            d.Springing = false;
            d.Showing = false;
        }

        static string Diag(Door d)
        {
            if (d.Hinge == null || d.Body == null) return "";
            JointLimits l = d.Hinge.limits;
            // Attaches en plus de la charniere (verrou du jeu) et leur couple de casse : une soudure se lit "inf".
            int extra = 0; string casse = "";
            foreach (Joint j in d.Body.GetComponents<Joint>())
                if (j != d.Hinge) { extra++; casse += " " + (float.IsInfinity(j.breakTorque) ? "inf" : j.breakTorque.ToString("F0")); }
            return "[" + d.Fsm.ActiveStateName + (d.Body.isKinematic ? " cin" : "") + (d.Body.IsSleeping() ? " dort" : "") + " m" + d.Body.mass.ToString("F0")
                   + (d.Hinge.useLimits ? " butees " + l.min.ToString("F0") + ".." + l.max.ToString("F0") : "") + (d.Hinge.useSpring ? " ressort " + d.Hinge.spring.spring.ToString("F0") + "@" + d.Hinge.spring.targetPosition.ToString("F0") : "")
                   + (d.Hinge.useMotor ? " moteur" : "") + (d.Hinge.connectedBody != null ? " sur " + d.Hinge.connectedBody.name : " libre")
                   + " attaches en plus " + extra + (extra > 0 ? " (casse" + casse + ")" : "") + (d.Ignored.Count > 0 ? " traverse " + d.Ignored.Count : "") + "]";
        }

        // Pose fermee : pivotee autour de sa charniere (repere de la voiture), vitesse de rotation nulle.
        static void Snap(Door d)
        {
            Transform b = d.Body.transform;
            Vector3 anchor = Vector3.Scale(b.localScale, d.Hinge.anchor);
            Vector3 pivot = b.localPosition + b.localRotation * anchor;
            b.localRotation = d.RestRot;
            b.localPosition = pivot - d.RestRot * anchor;
            d.Body.rotation = b.rotation; d.Body.position = b.position;
            d.Body.angularVelocity = Vector3.zero;
            d.Body.WakeUp();
        }

        static bool CanSnap(Door d) { return d.Body != null && d.Hinge != null && d.RestSet && !d.Body.isKinematic; }

        // Fin d'une fermeture rejouee : posee fermee, PUIS le jeu la claque (butees fermees posees sur la pose du
        // moment) ; reveillee pour que les butees la tiennent tout de suite.
        static void EndClosing(Door d, bool shut)
        {
            if (!d.Closing) return;
            d.Closing = false;
            StopFollow(d);
            if (shut)
            {
                // Les derniers degres (elle bute sur la caisse) : pivotee autour de sa charniere jusqu'a la pose
                // fermee. Portiere : toujours, meme loin (delai depasse sur une copie qui roule) -- claquee
                // entrouverte, sa charniere garderait cette pose pour zero. Capot, coffre : seulement pres (leur
                // fermeture pose elle-meme la rotation).
                if (CanSnap(d))
                {
                    float a = Mathf.Abs(Angle(d));
                    if (a > 10f && d.IsDoor) Log.Info("portiere " + d.Key + " posee fermee de " + a.ToString("F0") + " deg" + (d.NoRot ? " (sans SetRotation)" : "") + " " + Diag(d));
                    if (a <= 10f || d.IsDoor) Snap(d);
                }
                Drive(d, d.Close);
            }
            if (d.Body != null) d.Body.WakeUp();
        }

        // ------------------------------------------------------------ verrou ("Reset 2" -> "Set lock 2")
        // Entree dans le verrou : attaches deja la (pour reconnaitre celle que le jeu va poser) ; verrou
        // d'ouverture (angle lu 359 degres, moins de 0,7 s apres l'ouverture), rejoue ou sur la portiere d'un autre :
        // jamais annonce, retire une fois pose.
        static void LockEnter(Door d, bool replayed)
        {
            d.PreLock = d.Body != null ? d.Body.GetComponents<Joint>() : null;
            d.LockSpurious = replayed || !d.Mine || SpuriousLock(d, Time.realtimeSinceStartup);
        }

        static bool SpuriousLock(Door d, float now) { return d.State != DoorState.Open || now - d.OpenedAt < 0.7f; }

        // Fin de "Set lock 2" (action en fin d'etat : composant ajoute, relie a la caisse, couple de casse pose).
        static void LockSet(Door d)
        {
            if (d.Body == null) return;
            // Verrous precedents deja casses (le clic d'ouverture les casse en tirant) : oublies.
            for (int i = d.Locks.Count - 1; i >= 0; i--)
                if (d.Locks[i] == null) { lockJoints.Remove(d.Locks[i]); d.Locks.RemoveAt(i); }
            // Les attaches apparues depuis l'entree du verrou (sans releve d'entree : seulement sa variable Joint).
            if (d.PreLock != null)
                foreach (Joint j in d.Body.GetComponents<Joint>())
                    if (j != d.Hinge && System.Array.IndexOf(d.PreLock, j) < 0) AddLock(d, j);
            FsmObject v = d.Fsm.FsmVariables.FindFsmObject("Joint");
            Joint vj = v != null ? v.Value as Joint : null;
            if (vj != null && vj != d.Hinge && (d.PreLock == null || System.Array.IndexOf(d.PreLock, vj) < 0)) AddLock(d, vj);
            d.PreLock = null;
            if (d.LockSpurious || !d.Mine) DropLocks(d, d.Mine ? "verrou d'ouverture" : "portiere de #" + d.Owner);
        }

        static void AddLock(Door d, Joint j)
        {
            if (d.Locks.Contains(j)) return;
            d.Locks.Add(j);
            lockJoints.Add(j);
        }

        // Attaches posees par le verrou du jeu retirees (seulement celles-la : jamais la charniere ni une attache
        // de montage).
        static void DropLocks(Door d, string why)
        {
            int n = 0;
            foreach (Joint j in d.Locks)
            {
                lockJoints.Remove(j);
                if (j != null) { Object.Destroy(j); n++; }
            }
            d.Locks.Clear();
            if (n > 0) Log.Info("portiere " + d.Key + " : verrou retire (" + why + ")");
        }

        // Apres une ouverture rejouee : si LOCK l'a menee au verrou (traite apres notre "Mouse off"), elle revient
        // au repos et l'attache eventuelle est retiree.
        static void CheckLock(Door d, float now)
        {
            if (now > d.LockCheckUntil || d.Mine || d.Fsm == null) { d.LockCheckUntil = 0f; return; }
            string s = d.Fsm.ActiveStateName;
            if (s != d.LockState && s != d.SetLockState) return;
            d.LockCheckUntil = 0f;
            if (d.Fsm.Fsm.GetState("Mouse off") != null) Drive(d, "Mouse off");
            DropLocks(d, "ouverture rejouee");
            Log.Info("portiere " + d.Key + " : sortie du verrou (" + s + ") apres l'ouverture rejouee");
        }

        static void Trace(Door d) { d.TraceUntil = Time.realtimeSinceStartup + 6f; }

        // Evenement du joueur local (le jeu vient d'entrer dans l'etat accroche).
        static void Local(Door d, int kind)
        {
            float now = Time.realtimeSinceStartup;
            Trace(d);
            if (kind == K_LOCK && d.LockSpurious) return;   // (a l'ouverture, l'angle peut se lire 359 degres et le jeu passe une fois par la ; LockEnter)
            switch (kind)
            {
                case K_OPEN: d.State = DoorState.Open; d.OpenedAt = now; Take(d); break;
                case K_GRAB: if (d.State == DoorState.Locked) d.State = DoorState.Open; Take(d); break;
                case K_CLOSED: d.State = DoorState.Closed; d.Owner = -1; d.Mine = false; EndClosing(d, false); StopFollow(d); break;
                case K_LOCK: d.State = DoorState.Locked; break;   // (on continue d'envoyer son angle : figee par l'attache)
            }
            if (!Session.Active) return;
            Log.Info("portiere " + d.Key + " " + KindName(kind) + " ici");
            var w = new NetWriter(Msg.CarDoor).U8(Session.LocalId).Str(d.Key).U8(kind);
            if (Session.IsHost) Session.Broadcast(w, true);
            else { d.PendingOwn++; Session.SendToHost(w, true); }
        }

        static void Take(Door d)
        {
            d.Owner = Session.Active ? Session.LocalId : 0;
            d.Mine = true;
            d.Sent = false;
            EndClosing(d, false);
            StopFollow(d);
        }

        static string KindName(int kind)
        {
            return kind == K_OPEN ? "ouverte" : kind == K_CLOSED ? "fermee" : kind == K_GRAB ? "saisie" : kind == K_LOCK ? "verrouillee" : "?";
        }

        // Prise : branchee ou non selon le jeu ('On'), pas selon l'etat de l'automate (branchee, il attend le
        // joueur dans "Wait player" : lue comme debranchee, son debranchement par un autre etait saute ici).
        static void PlugState(Door d)
        {
            if (d.PlugOn != null) d.State = d.PlugOn.Value ? DoorState.Open : DoorState.Closed;
            else if (d.Fsm != null && d.Fsm.ActiveStateName == "Heater on") d.State = DoorState.Open;
        }

        // Evenement dans l'ordre de l'hote : le jeu est mene au meme etat (s'il n'y est pas deja).
        static void Apply(Door d, int kind, int who)
        {
            bool me = who == Session.LocalId;
            if (d.PlugOn != null) PlugState(d);
            Trace(d);
            if (kind == K_OPEN || kind == K_GRAB) EndClosing(d, false);
            // Maniee par un autre : un verrou pose ici la souderait (copie d'une voiture conduite ailleurs).
            if (!me && kind != K_LOCK) DropLocks(d, KindName(kind) + " par #" + who);
            switch (kind)
            {
                case K_OPEN:
                    d.Owner = who; d.Mine = me; d.Sent = false;
                    if (me) StopFollow(d);
                    if (d.State != DoorState.Closed) { d.State = DoorState.Open; return; }
                    d.State = DoorState.Open; d.OpenedAt = Time.realtimeSinceStartup;
                    // Ses butees d'ouverture se posent sur la pose du moment : fermee pour de bon d'abord.
                    if (d.IsDoor && CanSnap(d))
                    {
                        float a = Mathf.Abs(Angle(d));
                        if (a <= 10f) Snap(d);
                        else Log.Info("portiere " + d.Key + " ouverte de " + a.ToString("F0") + " deg ici " + Diag(d));
                    }
                    Drive(d, d.Open);
                    // Ses actions d'ouverture jouees (butees, son, Open), l'automate revient au repos -- sinon il
                    // attendrait ici un relachement de souris. (L'angle vient de celui qui l'a ouverte.)
                    if (d.IsDoor && d.Fsm.Fsm.GetState("Mouse off") != null) Drive(d, "Mouse off");
                    if (!me && d.HasLock) d.LockCheckUntil = Time.realtimeSinceStartup + 0.5f;
                    break;
                case K_GRAB:
                    d.Owner = who; d.Mine = me; d.Sent = false;
                    if (d.State == DoorState.Locked) d.State = DoorState.Open;
                    if (me) StopFollow(d);
                    return;
                case K_CLOSED:
                    d.Owner = -1; d.Mine = false;
                    if (d.State == DoorState.Closed) { if (!d.Closing) StopFollow(d); return; }
                    d.State = DoorState.Closed;
                    if (d.Body != null && d.Hinge != null && d.RestSet && Mathf.Abs(Angle(d)) >= 2.5f)
                    {
                        // Ramenee a 0, claquee par Update une fois fermee. Un capot ouvert est tenu a 60-65 par ses
                        // butees : son etat de fermeture ("State 2") les leve, comme quand on le rabat a la main.
                        d.Closing = true; d.ClosingSince = Time.realtimeSinceStartup; d.TargetAngle = 0f;
                        if (!d.IsDoor && d.Grab != null) Drive(d, d.Grab);
                        Follow(d);
                    }
                    else
                    {
                        StopFollow(d);
                        if (d.IsDoor && CanSnap(d)) Snap(d);   // (butees fermees posees sur la pose fermee)
                        Drive(d, d.Close);
                    }
                    break;
                case K_LOCK:
                    if (d.State != DoorState.Open) return;
                    d.State = DoorState.Locked;   // (pas d'attache ici : elle suit toujours l'angle de celui qui l'a poussee)
                    break;
                default: return;
            }
            Log.Info("portiere " + d.Key + " " + KindName(kind) + " par #" + who);
        }

        static void Drive(Door d, string state)
        {
            applying = true; Replay.Depth++;
            try { Game.SetState(d.Fsm, state); }
            finally { applying = false; Replay.Depth--; }
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            string key = r.Str();
            int kind = r.U8();
            float angle = 0f;
            if (kind == K_ANGLE) angle = r.F32();
            Door d;
            if (!byKey.TryGetValue(key, out d) || d.Fsm == null)
            {
                d = null;
                if (kind != K_ANGLE && Time.realtimeSinceStartup - lastUnknownScan >= 10f)   // pas un releve complet a chaque message
                {
                    lastUnknownScan = Time.realtimeSinceStartup;
                    Scan();
                    if (!byKey.TryGetValue(key, out d) || d.Fsm == null) d = null;
                }
                if (d == null && kind != K_ANGLE) Log.Warn("portiere " + key + " introuvable ici");
            }
            if (kind == K_ANGLE)
            {
                if (Session.IsHost) Session.Broadcast(new NetWriter(Msg.CarDoor).U8(who).Str(key).U8(K_ANGLE).F32(angle), false, who);
                if (d == null || who != d.Owner || d.Mine || d.State == DoorState.Closed || d.Body == null) return;
                d.TargetAngle = angle; d.LastRemote = Time.realtimeSinceStartup; d.Showing = true;
                return;
            }
            if (Session.IsHost)
            {
                // Arbitre : applique dans l'ordre d'arrivee, renvoie a tous (l'auteur compris : son retour).
                if (d != null) Apply(d, kind, who);
                Session.Broadcast(new NetWriter(Msg.CarDoor).U8(who).Str(key).U8(kind), true);
                return;
            }
            if (d == null) return;
            if (who == Session.LocalId)
            {
                // Retour d'un des notres : s'il est le dernier attendu, on se cale sur l'ordre de l'hote.
                if (d.PendingOwn > 0) d.PendingOwn--;
                if (d.PendingOwn == 0) Apply(d, kind, who);
                return;
            }
            // Evenement d'un autre, ordonne AVANT nos evenements en route : nos retours le corrigeront.
            if (d.PendingOwn > 0) return;
            Apply(d, kind, who);
        }

        // ------------------------------------------------------------ essais
        static Door First(string car, string part)
        {
            foreach (Door d in byKey.Values)
                if (d.Fsm != null && d.Key.StartsWith(car) && (part.Length == 0 || d.Key.Contains(part))) return d;
            return null;
        }

        // Essais : ouvre (ou ferme) la premiere portiere de 'car' comme un clic du joueur.
        public static string TestOpen(string car, bool open, string part = "")
        {
            Scan();
            Door d = First(car, part);
            if (d == null) return "aucune portiere sur " + car + " (" + byKey.Count + ")";
            Game.SetState(d.Fsm, open ? d.Open : d.Close);
            return d.Key + (open ? " -> " + d.Open : " -> " + d.Close);
        }

        // Essais : pousse la portiere ouverte de 'car' de 'deg' degres (ressort local, comme une main).
        public static string TestPush(string car, float deg)
        {
            foreach (Door d in byKey.Values)
            {
                if (d.Body == null || !d.Key.StartsWith(car)) continue;
                d.TestOffset = deg;
                return d.Key + " poussee de " + deg + " deg (angle envoye)";
            }
            return "rien a pousser";
        }

        // Essais : met la premiere portiere de 'car' dans l'etat 'state' (comme le jeu).
        public static string TestState(string car, string state, string part = "")
        {
            foreach (Door d in byKey.Values)
                if (d.Fsm != null && d.Key.StartsWith(car) && (part.Length == 0 || d.Key.Contains(part)) && d.Fsm.Fsm.GetState(state) != null) { Game.SetState(d.Fsm, state); return d.Key + " -> " + state; }
            return "rien";
        }

        // Essais : le joueur local debout dans la course de la portiere (dehors, a 45 cm, aux deux tiers).
        public static string TestStandInPath(string car, string part)
        {
            Scan();
            Door d = First(car, part);
            if (d == null || d.Body == null || d.Hinge == null) return "pas de portiere " + part;
            GameObject pl = GameObject.Find("PLAYER");
            if (pl == null) return "pas de joueur";
            Transform b = d.Body.transform, carT = VehicleSync.CarRoot(d.Fsm.transform) ?? d.Fsm.transform.root;
            Vector3 axis = b.TransformDirection(d.Hinge.axis).normalized;
            Vector3 hinge = b.TransformPoint(d.Hinge.anchor);
            Bounds bb = new Bounds(b.position, Vector3.zero);
            foreach (Renderer rr in b.GetComponentsInChildren<Renderer>()) bb.Encapsulate(rr.bounds);
            Vector3 along = Vector3.ProjectOnPlane(bb.center - hinge, axis);
            Vector3 side = carT.right * Mathf.Sign(Vector3.Dot(bb.center - carT.position, carT.right));
            Vector3 p = hinge + along * 1.25f + side * 0.45f;
            p.y = carT.position.y + 0.1f;
            var cc = pl.GetComponent<CharacterController>();
            cc.enabled = false;
            pl.transform.position = p;
            cc.enabled = true;
            return "debout dans la course de " + d.Key + " en " + p.ToString("F2");
        }

        // Essais : clics rapides au hasard sur une portiere (ouvrir / pousser vers la fermeture, relache vite),
        // comme un joueur qui « spamme » le clic gauche. A appeler a chaque image.
        static float raceNext, raceRelease;
        static System.Random raceRnd;
        public static string TestRace(string car, string part)
        {
            Door d = First(car, part);
            if (d == null || d.Grab == null) return null;
            if (raceRnd == null) raceRnd = new System.Random(1234 + Session.LocalId * 77);
            float now = Time.realtimeSinceStartup;
            string s = d.Fsm.ActiveStateName;
            if (raceRelease > 0 && now >= raceRelease)
            {
                raceRelease = 0;
                // Relachement : "Open door" -> Mouse off, "Open door 2" -> Check position (comme le jeu).
                if (s == d.Open) { Game.SetState(d.Fsm, "Mouse off"); return "relache (ouverture)"; }
                if (s == d.Grab) { Game.SetState(d.Fsm, "Check position"); return "relache (fermeture)"; }
                return null;
            }
            if (raceRelease > 0 || now < raceNext) return null;
            raceNext = now + 0.15f + (float)raceRnd.NextDouble() * 0.45f;
            raceRelease = now + 0.08f + (float)raceRnd.NextDouble() * 0.5f;
            bool open = OpenVar(d);
            Game.SetState(d.Fsm, open ? d.Grab : d.Open);
            return "clic " + (open ? "fermer" : "ouvrir");
        }

        // Essais : saisit la portiere pour la refermer (bouton tenu : le jeu la pousse jusqu'a la fermer).
        public static string TestGrab(string car, string part)
        {
            Door d = First(car, part);
            if (d == null || d.Grab == null) return "rien a saisir";
            Game.SetState(d.Fsm, d.Grab);
            return d.Key + " -> " + d.Grab;
        }

        // Essais : portieres, coffres et capots suivis sur la voiture de cle 'car'.
        public static int CountFor(string car)
        {
            int n = 0;
            foreach (Door d in byKey.Values) if (d.Fsm != null && d.Key.StartsWith(car + "/")) n++;
            return n;
        }

        public static string StateOf(string car, string part)
        {
            Door d = First(car, part);
            return d == null ? "?" : State(d.Key);
        }

        public static string State(string key)
        {
            foreach (Door d in byKey.Values)
                if (d.Key.StartsWith(key) && d.Fsm != null)
                {
                    return d.Key + " etat " + d.Fsm.ActiveStateName + " Open=" + OpenVar(d)
                           + (d.Hinge != null && d.RestSet ? ", angle " + Angle(d).ToString("F1") + " deg" : ", pas de pose fermee")
                           + ", " + d.State + ", main #" + d.Owner + (d.Mine ? " (nous)" : "") + (d.Springing ? ", suit" : "") + (d.Closing ? ", se ferme" : "") + (d.PendingOwn > 0 ? ", attend " + d.PendingOwn : "")
                           + (d.Locks.Count > 0 ? ", verrou pose ici" : "") + (d.NoRot ? ", fermeture sans SetRotation" : "") + " " + Diag(d);
                }
            return "?";
        }

        // ------------------------------------------------------------ essais automatiques (appeles par Autotest)
        // portiere-verrou (invite ; l'hote en Autotest=conduite sur [Test] TestVoiture) : des que la voiture est
        // conduite par l'autre, [Test] TestCycles fois (3) : ouvre [Test] TestPorte (DoorFront(leftx)), la lache
        // 0,6 s apres, la saisit 3 s apres pour la refermer ; etat (attaches en plus comprises) a chaque etape et
        // chaque seconde. Puis attend que le conducteur l'ouvre ; [Test] PortiereDansCourse=1 : debout dans sa
        // course, controleur coupe puis remis (comme assis puis debout) -- elle doit le traverser et suivre l'angle.
        // Hote ([Test] PortiereAuVolant=1, en Autotest=conduite) : memes cycles au volant a partir de
        // [Test] PortiereAuVolantA (60 s).
        static int tStep, tCycle;
        static float tNext, tLog, tUntil;

        public static void Test(string mode, float t)
        {
            bool guest = mode == "portiere-verrou";
            bool driver = mode == "conduite" && Config.GetInt("Test", "PortiereAuVolant", 0) != 0;
            if ((!guest && !driver) || t < 12f) { if (t < 1f) tStep = 0; return; }
            string vc = Config.Get("Test", "TestVoiture", "SORBET(190-200psi)"), vp = Config.Get("Test", "TestPorte", "DoorFront(leftx)");
            Rigidbody car = VehicleSync.Body(vc);
            if (car == null) return;
            float now = Time.realtimeSinceStartup;
            if (tStep == 0)
            {
                bool go = guest ? t > 20f && VehicleSync.RemotelyDriven(car.transform) : t > Config.GetInt("Test", "PortiereAuVolantA", 60);
                if (!go) return;
                tStep = 1; tCycle = 0; tNext = now; tLog = now;
                Log.Info("autotest : verrou, " + Config.GetInt("Test", "TestCycles", 3) + " cycles de " + vp + (guest ? " pendant que l'autre conduit" : " au volant"));
            }
            if (tStep >= 1 && tStep <= 8 && now >= tLog) { tLog = now + 1f; Log.Info("autotest : verrou " + StateOf(vc, vp) + ", voiture " + (car.isKinematic ? "copie" : "locale")); }
            if (now < tNext) return;
            switch (tStep)
            {
                case 1: tStep = 2; tNext = now + 0.6f; Log.Info("autotest : verrou cycle " + (tCycle + 1) + " ouvre " + TestOpen(vc, true, vp)); break;
                case 2: tStep = 3; tNext = now + 3f; Log.Info("autotest : verrou lache " + TestState(vc, "Mouse off", vp) + " | " + StateOf(vc, vp)); break;
                case 3:
                    Log.Info("autotest : verrou referme " + TestGrab(vc, vp));
                    tNext = now + 3f;
                    tStep = ++tCycle < Config.GetInt("Test", "TestCycles", 3) ? 1 : guest ? 4 : 8;
                    tUntil = now + (guest ? 45f : 6f);
                    break;
                case 4:
                    {
                        // Ouverte par le conducteur : la copie doit suivre son angle (TracePortiere : angle -> voulu).
                        Door d = First(vc, vp);
                        if (now > tUntil) { tStep = 9; Log.Info("autotest : verrou, personne d'autre ne l'a ouverte"); break; }
                        if (d == null || d.State == DoorState.Closed || d.Mine || d.Owner < 0) break;
                        Log.Info("autotest : verrou ouverte par #" + d.Owner + " " + State(d.Key));
                        tStep = Config.GetInt("Test", "PortiereDansCourse", 0) != 0 ? 5 : 8;
                        tNext = now + 1.5f; tUntil = now + 20f;
                        break;
                    }
                case 5: tStep = 6; tNext = now + 2f; Log.Info("autotest : verrou " + TestStandInPath(vc, vp)); break;
                case 6:
                case 7:
                    {
                        // Comme Seats : moteur de deplacement coupe avec le controleur (sinon Move sur un controleur coupe).
                        GameObject pl = GameObject.Find("PLAYER");
                        if (pl == null) { tStep = 8; break; }
                        bool on = tStep == 7;
                        Behaviour motor = pl.GetComponent("CharacterMotor") as Behaviour, input = pl.GetComponent("FPSInputController") as Behaviour;
                        CharacterController cc = pl.GetComponent<CharacterController>();
                        if (!on) { if (motor != null) motor.enabled = false; if (input != null) input.enabled = false; }
                        if (cc != null) cc.enabled = on;
                        if (on) { if (motor != null) motor.enabled = true; if (input != null) input.enabled = true; }
                        Log.Info("autotest : verrou controleur " + (on ? "remis" : "coupe") + " | " + StateOf(vc, vp));
                        tNext = now + (on ? 0f : 0.3f);
                        tStep++;
                        break;
                    }
                case 8: if (now > tUntil) { tStep = 9; Log.Info("autotest : verrou fin " + StateOf(vc, vp)); } break;
            }
        }
    }
}
