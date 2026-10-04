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
    // attache jamais cassee chez eux tirerait la voiture a la fermeture suivante).
    // Position en temps reel : celui qui l'a ouverte ou saisie en dernier (Owner) envoie l'angle de sa
    // charniere, mesure depuis sa pose fermee (le repere des butees et du ressort : 0 fermee ; HingeJoint.angle,
    // lui, lit 120 coffre ferme et 93 ouvert pour des butees 0 et 60, inutilisable). Chez les autres, la portiere reste un corps physique ordinaire, menee a cet
    // angle par une petite correction de sa vitesse autour de l'axe de la charniere (a chaque image ; la charniere
    // elle-meme n'est JAMAIS modifiee : dans ce Unity, changer son ressort ou ses butees la recree avec la pose
    // du moment pour zero -- portieres « fermees » a 120 degres apres un spam de clics), et ne heurte plus le joueur local (elle traverse les joueurs comme
    // chez celui qui la manie : les avatars n'ont pas de collision). Jamais cinematique ni teleportee : une
    // portiere cinematique (masse infinie) accrochee a une voiture qui roule la tirait -- coffre referme pendant
    // qu'un autre conduisait : voiture envolee (retour de JD, 0.16).
    public static class CarDoors
    {
        const int K_CLOSED = 0, K_OPEN = 1, K_ANGLE = 2, K_GRAB = 3, K_LOCK = 4;
        enum DoorState { Closed, Open, Locked }

        class Door
        {
            public string Key; public PlayMakerFSM Fsm; public string Open, Grab, Close;
            public bool HasLock, IsDoor;
            public Rigidbody Body; public HingeJoint Hinge;
            public DoorState State;
            public int Owner = -1; public bool Mine;   // dernier a l'avoir maniee ; nous (on envoie son angle)
            public int PendingOwn;                     // invite : evenements envoyes dont le retour de l'hote manque
            public float LastAngle; public bool Sent;
            public bool Showing; public float TargetAngle; public float LastRemote;
            public bool Springing;                     // menee vers TargetAngle (Follow)
            public CharacterController IgnoredCc;      // joueur local traverse pendant ce temps
            public bool Closing; public float ClosingSince;   // fermeture rejouee : ramenee a 0 avant de claquer
            public float LastSentAt, OpenedAt;
            public float TraceUntil, NextTrace;        // essais : [Test] TracePortiere
            public bool RestSet; public Quaternion RestRot;   // pose fermee (repere du parent)
            public float TestOffset;   // essais : degres ajoutes a l'angle envoye
        }

        static readonly Dictionary<string, Door> byKey = new Dictionary<string, Door>();
        static readonly HashSet<PlayMakerFSM> hooked = new HashSet<PlayMakerFSM>();
        static float nextScan = -1, nextAngle, lastUnknownScan = -100;
        static readonly List<KeyValuePair<float, Peer>> snapshots = new List<KeyValuePair<float, Peer>>();
        static bool applying;

        class Hook : FsmStateAction
        {
            public Door D;
            public int Kind;
            public override void OnEnter()
            {
                try
                {
                    if (applying || Replay.Depth > 0) { }
                    else if (D.Closing && Kind == K_CLOSED) EndClosing(D, false);   // (le jeu acheve la fermeture rejouee)
                    else Local(D, Kind);
                }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        public static bool Tracks(PlayMakerFSM f) { return hooked.Contains(f); }

        public static void OnLevelLoaded()
        {
            byKey.Clear(); hooked.Clear(); snapshots.Clear();
            nextScan = PlayerSync.InGame ? Time.realtimeSinceStartup + 12f : -1;
        }

        static void Scan()
        {
            int before = byKey.Count;
            foreach (Rigidbody car in Object.FindObjectsOfType<Rigidbody>())
            {
                if (car.transform.parent != null || car.GetComponent("CarDynamics") == null) continue;
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
                    string rel = Recon.Path(f.transform).Substring(car.name.Length);
                    int k; seen.TryGetValue(rel, out k); seen[rel] = k + 1;   // compte aussi les portieres deja suivies
                    if (hooked.Contains(f) || !Replay.Claim(f, "portieres")) continue;
                    var d = new Door { Key = car.name + rel + "#" + k, Fsm = f, Open = open, Grab = grab, Close = close, IsDoor = isDoor };
                    d.Body = f.GetComponentInParent<Rigidbody>();
                    if (d.Body != null && d.Body.transform == car.transform) d.Body = null;
                    d.Hinge = d.Body != null ? d.Body.GetComponent<HingeJoint>() : null;
                    d.State = OpenVar(d) ? DoorState.Open : DoorState.Closed;
                    if (!Inject(d, open, K_OPEN) || !Inject(d, close, K_CLOSED)) continue;
                    if (grab != null) Inject(d, grab, K_GRAB);
                    d.HasLock = f.Fsm.GetState("Reset 2") != null && Inject(d, "Reset 2", K_LOCK);
                    hooked.Add(f);
                    byKey[d.Key] = d;
                    NoteRest(d);
                }
            }
            // Prises du chauffage moteur (cable plug) : "Heater on" = branchee sur la voiture, "Heater off"
            // = debranchee. Partout dans la scene (debranchee, elle pend au poteau de la maison) ; cle :
            // la prise de la voiture (Socket).
            foreach (PlayMakerFSM f in Object.FindObjectsOfType<PlayMakerFSM>())
            {
                if (f.FsmName != "Data" || hooked.Contains(f) || !f.gameObject.name.StartsWith("cable plug")) continue;
                if (f.Fsm.GetState("Heater on") == null || f.Fsm.GetState("Heater off") == null || !Replay.Claim(f, "portieres")) continue;
                FsmGameObject sock = f.FsmVariables.FindFsmGameObject("Socket");
                if (sock == null || sock.Value == null) continue;
                var d = new Door { Key = "prise:" + Recon.Path(sock.Value.transform), Fsm = f, Open = "Heater on", Close = "Heater off" };
                d.State = f.ActiveStateName == "Heater on" ? DoorState.Open : DoorState.Closed;
                if (!Inject(d, d.Open, K_OPEN) || !Inject(d, d.Close, K_CLOSED)) continue;
                hooked.Add(f);
                byKey[d.Key] = d;
            }
            if (byKey.Count != before) Log.Info("portieres : " + byKey.Count + " suivies (portes, coffres, hayons, prises)");
        }

        static bool Inject(Door d, string state, int kind)
        {
            FsmState s = d.Fsm.Fsm.GetState(state);
            if (s == null) return false;
            try
            {
                var list = new List<FsmStateAction>(s.Actions);
                list.Insert(0, new Hook { D = d, Kind = kind });
                s.Actions = list.ToArray();
                return true;
            }
            catch { return false; }
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
            if (now >= nextScan) { nextScan = now + 30f; Scan(); }
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
                        Session.T.SendReliable(p, new NetWriter(Msg.CarDoor).U8(d.Owner >= 0 ? d.Owner : Session.LocalId).Str(d.Key).U8(K_OPEN).ToArray());
                        n++;
                    }
                Log.Info("portieres : " + n + " ouvertes envoyees a " + p);
            }
            // Chez nous, pour un autre : le ressort de la charniere la mene a son angle ; fermeture rejouee :
            // ramenee a 0, puis le jeu la claque (comme quand on la pousse jusqu'au bout).
            bool trace = Config.GetInt("Test", "TracePortiere", 0) != 0;
            foreach (Door d in byKey.Values)
            {
                NoteRest(d);
                if (trace && now < d.TraceUntil && now >= d.NextTrace && d.Hinge != null)
                { d.NextTrace = now + 0.1f; Log.Info("trace " + d.Key + " " + Angle(d).ToString("F1") + (d.Springing ? " -> " + d.TargetAngle.ToString("F1") : "") + " " + Diag(d)); }
                if (d.Closing)
                {
                    if (d.Hinge == null || !d.RestSet || Mathf.Abs(Angle(d)) < 1.5f || now - d.ClosingSince > 1.2f) EndClosing(d, true);
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

        // Menee vers TargetAngle : sa vitesse autour de l'axe de la charniere (par rapport a la voiture) est
        // corrigee a chaque image, proportionnelle a l'ecart et bornee ; le joueur local ne la bloque pas.
        const float ServoGain = 12f, ServoMax = 360f;   // (1/s, degres/s)
        static void Follow(Door d)
        {
            if (d.Hinge == null || d.Body == null || !d.RestSet || d.Body.isKinematic) return;
            if (!d.Springing) { d.Springing = true; IgnorePlayer(d, true); }
            Vector3 axis = d.Body.transform.TransformDirection(d.Hinge.axis).normalized;
            Rigidbody car = d.Hinge.connectedBody;
            Vector3 rel = d.Body.angularVelocity - (car != null ? car.angularVelocity : Vector3.zero);
            float w = Vector3.Dot(rel, axis) * Mathf.Rad2Deg;
            float want = Mathf.Clamp(Mathf.DeltaAngle(Angle(d), d.TargetAngle) * ServoGain, -ServoMax, ServoMax);
            d.Body.AddTorque(axis * ((want - w) * Mathf.Deg2Rad), ForceMode.VelocityChange);
            if (d.Body.IsSleeping()) d.Body.WakeUp();
        }

        static void IgnorePlayer(Door d, bool on)
        {
            if (on)
            {
                GameObject pl = GameObject.Find("PLAYER");
                d.IgnoredCc = pl != null ? pl.GetComponent<CharacterController>() : null;
            }
            if (d.IgnoredCc == null || d.Body == null) return;
            foreach (Collider c in d.Body.GetComponentsInChildren<Collider>(true))
                if (c != null && !c.isTrigger && c.enabled && c.gameObject.activeInHierarchy) Physics.IgnoreCollision(c, d.IgnoredCc, on);
            if (!on) d.IgnoredCc = null;
        }

        // Fin du suivi : le joueur local la heurte de nouveau.
        static void StopFollow(Door d)
        {
            if (d.Springing) IgnorePlayer(d, false);
            d.Springing = false;
            d.Showing = false;
        }

        static string Diag(Door d)
        {
            if (d.Hinge == null || d.Body == null) return "";
            JointLimits l = d.Hinge.limits;
            return "[" + d.Fsm.ActiveStateName + (d.Body.isKinematic ? " cin" : "") + (d.Body.IsSleeping() ? " dort" : "") + " m" + d.Body.mass.ToString("F0")
                   + (d.Hinge.useLimits ? " butees " + l.min.ToString("F0") + ".." + l.max.ToString("F0") : "") + (d.Hinge.useSpring ? " ressort " + d.Hinge.spring.spring.ToString("F0") + "@" + d.Hinge.spring.targetPosition.ToString("F0") : "")
                   + (d.Hinge.useMotor ? " moteur" : "") + (d.Hinge.connectedBody != null ? " sur " + d.Hinge.connectedBody.name : " libre") + "]";
        }

        // Fin d'une fermeture rejouee : le jeu la claque (pose et butees fermees) ; reveillee pour que les
        // butees la tiennent tout de suite.
        static void EndClosing(Door d, bool shut)
        {
            if (!d.Closing) return;
            d.Closing = false;
            StopFollow(d);
            if (shut)
            {
                Drive(d, d.Close);
                // Les derniers degres (elle bute sur la caisse) : pivotee autour de sa charniere jusqu'a la pose
                // fermee -- seulement si l'ecart est petit (sinon la charniere tirerait sur la voiture).
                if (d.Body != null && d.Hinge != null && d.RestSet && Mathf.Abs(Angle(d)) <= 10f)
                {
                    Transform b = d.Body.transform;
                    Vector3 anchor = Vector3.Scale(b.localScale, d.Hinge.anchor);
                    Vector3 pivot = b.localPosition + b.localRotation * anchor;
                    b.localRotation = d.RestRot;
                    b.localPosition = pivot - d.RestRot * anchor;
                    d.Body.rotation = b.rotation; d.Body.position = b.position;
                    d.Body.angularVelocity = Vector3.zero;
                }
            }
            if (d.Body != null) d.Body.WakeUp();
        }

        static void Trace(Door d) { d.TraceUntil = Time.realtimeSinceStartup + 6f; }

        // Evenement du joueur local (le jeu vient d'entrer dans l'etat accroche).
        static void Local(Door d, int kind)
        {
            float now = Time.realtimeSinceStartup;
            Trace(d);
            if (kind == K_LOCK && (d.State != DoorState.Open || now - d.OpenedAt < 0.7f)) return;   // (a l'ouverture, l'angle peut se lire 359 degres et le jeu passe une fois par la)
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

        // Evenement dans l'ordre de l'hote : le jeu est mene au meme etat (s'il n'y est pas deja).
        static void Apply(Door d, int kind, int who)
        {
            bool me = who == Session.LocalId;
            Trace(d);
            if (kind == K_OPEN || kind == K_GRAB) EndClosing(d, false);
            switch (kind)
            {
                case K_OPEN:
                    d.Owner = who; d.Mine = me; d.Sent = false;
                    if (me) StopFollow(d);
                    if (d.State != DoorState.Closed) { d.State = DoorState.Open; return; }
                    d.State = DoorState.Open; d.OpenedAt = Time.realtimeSinceStartup;
                    Drive(d, d.Open);
                    // Ses actions d'ouverture jouees (butees, son, Open), l'automate revient au repos -- sinon il
                    // attendrait ici un relachement de souris. (L'angle vient de celui qui l'a ouverte.)
                    if (d.IsDoor && d.Fsm.Fsm.GetState("Mouse off") != null) Drive(d, "Mouse off");
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
                    else { StopFollow(d); Drive(d, d.Close); }
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
            Transform b = d.Body.transform, carT = d.Fsm.transform.root;
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
                           + ", " + d.State + ", main #" + d.Owner + (d.Mine ? " (nous)" : "") + (d.Springing ? ", suit" : "") + (d.Closing ? ", se ferme" : "") + (d.PendingOwn > 0 ? ", attend " + d.PendingOwn : "") + " " + Diag(d);
                }
            return "?";
        }
    }
}
