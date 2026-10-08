using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Atelier : crics, pont du garage Fleetari, palan moteur, degats d'accident, chargeur avant du tracteur.
    //
    // APPAREILS DE LEVAGE (releve 8 s apres le chargement : avant Jobs et WorldFsms, qui ne les rejouent plus) :
    //  - cric rouleur (floor jack(itemx), maison et garage Fleetari) : 'Use' de son Trigger, Up = Y + pas et iTween
    //    du point cible (Lifter/target_position), que 'Movement' du plateau (Lifter/lift) suit ;
    //  - cric de voiture (car jack(itemx)) : 'Fold' (deplie/replie : BoolFlip de Open, objet, donc saute par
    //    WorldFsms) et 'Use' de son Trigger (actif une fois deplie) : meme Y + iTween (Lift/target_position), plateau
    //    Lift/lift, a qui le jeu ajoute un corps cinematique tant qu'il est leve (detruit en bas) ;
    //  - pont (REPAIRSHOP/LOD/Lifter/Functions 'Use') : bouton tenu, PlatformPos + vitesse a chaque image et
    //    SetPosition de REPAIRSHOP/Lifter/Platform. Rejoue par evenement, il montait chez l'autre tant que sa souris
    //    a lui ne le relachait pas (jamais) ou pas du tout ;
    //  - palan moteur (motor hoist(itemx)/Pump/Trigger 'Usage') : Angle + pas, SetRotation du bras (motorhoist_arm,
    //    qui porte la chaine et le moteur accroche).
    //  Le niveau (variable de l'automate) n'est change que par un geste du joueur : celui chez qui il change sans
    //  message recu a la main. Il envoie le niveau et la pose locale des pieces mobiles 10 fois/s tant qu'il
    //  manoeuvre (2,5 s apres le dernier pas : le plateau du cric finit sa course), puis une derniere fois (fiable).
    //  Ailleurs : le niveau est recopie dans l'automate (le geste suivant part du meme niveau) et les pieces sont
    //  tenues sur la pose recue apres la logique du jeu, 3 s apres le dernier message. Le corps cinematique du
    //  plateau du cric est ajoute/retire comme le jeu le fait.
    //  Voiture sur un appareil leve : la voiture garee n'est recalee par VehicleSync qu'au-dela de 1 m / 10 deg ;
    //  une voiture levee de 25 cm d'un seul cote restait donc en bas chez l'autre. Tant qu'un cric ou le pont est
    //  leve sous une voiture (et 4 s apres), celui qui fait autorite sur elle (VehicleSync.Authority : l'hote
    //  pour une voiture garee) envoie sa pose 4 fois/s ; ailleurs elle est replacee si l'ecart depasse 4 cm ou
    //  1,5 deg (vitesses mises a zero ; jamais une copie conduite par un autre ni la voiture qu'on conduit).
    //
    // DEGATS (voitures de VehicleSync, releve toutes les 20 s et quand sa liste change) :
    //  - vitres (automate 'Data' a etats "Assemble"/"Break glass", variable Collider : pare-brise CORRIS, taxi,
    //    SORBET, vitres de la GIFU) : la casse est l'attache (FixedJoint) du collider qui disparait (choc, coup de
    //    poing : 'Break', ou objet detruit). Celui chez qui elle disparait l'annonce ; ailleurs l'attache est
    //    detruite et 'Assemble' du jeu (HasComponent) casse la vitre lui-meme. (La reparation : REPAIR, rejouee
    //    par Jobs.) Une copie conduite ailleurs ne casse jamais seule (VehicleSync.ProtectJoints) ;
    //  - tole (composants Deformable : sommets deplaces au choc) : chaque sommet qui a bouge ici depuis le dernier
    //    releve (2 fois/s) part en millimetres par rapport au sommet d'origine, par lots ; ailleurs ils sont poses
    //    puis UpdateMesh (maillage et collider). La reparation (Repair) part de meme ;
    //  - trains tordus (transforms *DamagePivot* : FL/FR/RL/RR de Calculations les tournent au hasard au choc) :
    //    rotation locale envoyee quand elle change.
    //  Chacun envoie ce qui change chez lui : la voiture conduite casse chez son conducteur, la voiture garee
    //  heurtee chez celui ou le choc a eu lieu. Copie d'une voiture conduite ailleurs : ses toles et ses trains
    //  bouges ici (copie cinematique qui suit par a-coups, joueur ou voiture locale qui la heurte : le conducteur a
    //  son propre choc) sont remis comme recus, rien n'est envoye -- la portiere de la CORRIS se cabossait chez
    //  l'invite et partait chez le conducteur (08/10).
    //
    // CHARGEUR AVANT (KEKMET .../NewHydraulics/FrontHydArm et FrontHydLoader 'Use' : ArmRot, SetRotation du bras
    // Frontloader/ArmPivot/Arm et du godet .../LoaderPivot/Loader) : l'etat INCREASE/DECREASE tourne tant que le
    // bouton est tenu ; rejoue par Jobs il tournait chez l'autre jusqu'au message suivant (derive). Pris ici :
    // celui chez qui ArmRot change envoie ArmRot et la rotation du bras 10 fois/s ; ailleurs ArmRot est recopie et
    // le bras rejoint la rotation recue en douceur.
    public static class Garage
    {
        const string Module = "atelier";
        // Msg.Garage : joueur, type (bit 0x80 : fiable, l'hote relaie pareil), donnees.
        const int T_LIFT = 1, T_FOLD = 2, T_CAR = 3, T_GLASS = 4, T_DENT = 5, T_BEND = 6, T_LOADER = 7;
        const int RELIABLE = 0x80;

        static float nextScan = -1, loadedAt, nextPoll, nextCar, nextDamage, nextWarn;
        static int gen = -1, liftScans, dmgIdx;

        public static void OnLevelLoaded()
        {
            lifts.Clear(); liftKnown.Clear(); owned.Clear(); loaders.Clear(); loaderKnown.Clear();
            dmg.Clear(); lifted.Clear(); corrected.Clear();
            gen = -1; liftScans = 0; dmgIdx = 0; tStep = 0; tAt = 0; tLog = 0; testFsm = null; testOff.Clear();
            loadedAt = Time.realtimeSinceStartup;
            nextScan = PlayerSync.InGame ? loadedAt + 8f : -1;
        }

        public static void Update()
        {
            if (nextScan < 0 || !Session.Active) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextScan || (gen >= 0 && gen != VehicleSync.Generation))
            {
                nextScan = now + 20f;
                gen = VehicleSync.Generation;
                // Appareils : objets de la scene, presents des le chargement (meme inactifs) ; deux passages suffisent.
                if (liftScans++ < 2) ScanLifts();
                ScanLoaders(); ScanDamage();
            }
            if (now >= nextPoll) { nextPoll = now + 0.1f; PollLifts(now); PollLoaders(now); }
            if (now >= nextCar) { nextCar = now + 0.25f; SendLiftedCars(now); }
            // Degats : une voiture par passage (10/s), chacune revue environ une fois par seconde.
            if (now >= nextDamage) { nextDamage = now + 0.1f; PollDamage(); }
        }

        // Apres la logique du jeu (appele par CarVisuals.LateUpdate) : poses recues des appareils et du chargeur.
        public static void LateUpdate()
        {
            if (nextScan < 0) return;
            float now = Time.realtimeSinceStartup;
            float k = 1f - Mathf.Exp(-12f * Time.deltaTime);
            foreach (Lift d in lifts)
            {
                if (!d.Held) continue;
                if (now > d.HeldUntil) { d.Held = false; continue; }
                for (int i = 0; i < d.Moved.Length; i++)
                {
                    Transform t = d.Moved[i];
                    if (t == null) continue;
                    t.localPosition = Vector3.Lerp(t.localPosition, d.HPos[i], k);
                    t.localRotation = Quaternion.Slerp(t.localRotation, d.HRot[i], k);
                }
            }
            foreach (Loader l in loaders)
            {
                if (!l.Held || l.Tip == null) continue;
                if (now > l.HeldUntil) { l.Held = false; continue; }
                l.Tip.localRotation = Quaternion.Slerp(l.Tip.localRotation, l.HRot, k);
            }
        }

        // Corps mus par ce module (plateau des crics, pont) : Props ne doit pas les recaler (voir WIRING).
        public static bool Owns(Rigidbody rb)
        {
            return rb != null && owned.Contains(rb.transform);
        }

        // ================================================================ appareils de levage
        const int K_CRIC = 0, K_CRICR = 1, K_PONT = 2, K_PALAN = 3;
        static readonly string[] KindName = { "cric", "cric rouleur", "pont", "palan" };
        static readonly string[] KindTag = { "cric", "rouleur", "pont", "palan" };

        class Lift
        {
            public string Key; public int Kind;
            public Transform Root;                       // l'appareil (cric, palan) ; le pont : REPAIRSHOP/LOD/Lifter
            public PlayMakerFSM Ctrl; public FsmFloat Level;
            public PlayMakerFSM Fold; public FsmBool Open; public bool OpenLast;
            public Transform[] Moved; public Transform Point;   // pieces mobiles ; ce qui porte la voiture
            public float Rest, Last, ActiveUntil;
            public bool Final;
            public bool Held; public float HeldUntil; public Vector3[] HPos; public Quaternion[] HRot;
            public Rigidbody Added;                      // corps cinematique du plateau du cric ajoute ici
            public int By = -1;                          // dernier joueur qui l'a manoeuvre (essais)
        }
        static readonly List<Lift> lifts = new List<Lift>();
        static readonly HashSet<PlayMakerFSM> liftKnown = new HashSet<PlayMakerFSM>();
        static readonly HashSet<Transform> owned = new HashSet<Transform>();

        static void ScanLifts()
        {
            var roots = new HashSet<GameObject>(Recon.SceneRoots());
            int added = 0;
            foreach (Object o in Game.AllFsms())
            {
                var f = (PlayMakerFSM)o; if (f == null) continue;
                if (f == null || f.hideFlags != HideFlags.None || liftKnown.Contains(f)) continue;
                Transform p = f.transform.parent;
                if (p == null) continue;
                string on = f.gameObject.name;
                int kind = -1; Transform root = null; string var = null;
                if (f.FsmName == "Use" && on == "Trigger" && p.name == "car jack(itemx)") { kind = K_CRIC; root = p; var = "Y"; }
                else if (f.FsmName == "Use" && on == "Trigger" && p.name == "floor jack(itemx)") { kind = K_CRICR; root = p; var = "Y"; }
                else if (f.FsmName == "Use" && on == "Functions" && p.name == "Lifter") { kind = K_PONT; root = p; var = "PlatformPos"; }
                else if (f.FsmName == "Usage" && on == "Trigger" && p.name == "Pump" && p.parent != null && p.parent.name == "motor hoist(itemx)") { kind = K_PALAN; root = p.parent; var = "Angle"; }
                if (kind < 0 || !roots.Contains(f.transform.root.gameObject)) continue;   // (modeles hors scene)
                FsmFloat lv = f.FsmVariables.FindFsmFloat(var);
                if (lv == null) continue;
                liftKnown.Add(f);
                if (!Replay.Claim(f, Module)) { Log.Warn("atelier : " + Recon.Path(f.transform) + "::" + f.FsmName + " deja a " + Replay.Owner(f)); continue; }
                var d = new Lift { Kind = kind, Root = root, Ctrl = f, Level = lv, Rest = lv.Value, Last = lv.Value,
                                   Key = KindTag[kind] + ":" + Recon.Path(root) + "#" + root.GetSiblingIndex() };
                string[] rel = kind == K_CRIC ? new[] { "Lift/target_position", "Lift/target_position2", "Lift/lift" }
                             : kind == K_CRICR ? new[] { "Lifter/target_position", "Lifter/target_position2", "Lifter/lift", "Pivot/Move" }
                             : kind == K_PALAN ? new[] { "motorhoist_arm" } : new string[0];
                var moved = new List<Transform>();
                foreach (string r in rel) { Transform t = root.Find(r); if (t != null) moved.Add(t); }
                if (kind == K_PONT)
                {
                    FsmGameObject pg = f.FsmVariables.FindFsmGameObject("Platform");
                    if (pg != null && pg.Value != null) moved.Add(pg.Value.transform);
                    d.Point = moved.Count > 0 ? moved[0] : null;
                }
                else if (kind == K_CRIC) d.Point = root.Find("Lift/lift");
                else if (kind == K_CRICR) d.Point = root.Find("Lifter/lift");
                d.Moved = moved.ToArray();
                d.HPos = new Vector3[d.Moved.Length]; d.HRot = new Quaternion[d.Moved.Length];
                if (d.Point != null) owned.Add(d.Point);
                if (kind == K_CRIC)
                {
                    d.Fold = Game.FsmOn(root.gameObject, "Fold");
                    if (d.Fold != null && Replay.Claim(d.Fold, Module)) { d.Open = d.Fold.FsmVariables.FindFsmBool("Open"); if (d.Open != null) d.OpenLast = d.Open.Value; }
                    else d.Fold = null;
                }
                lifts.Add(d);
                added++;
            }
            if (added > 0)
            {
                var sb = new System.Text.StringBuilder();
                foreach (Lift d in lifts) sb.Append(sb.Length > 0 ? ", " : "").Append(KindName[d.Kind]).Append(' ').Append(d.Key).Append(" (").Append(d.Moved.Length).Append(" pieces)");
                Log.Info("atelier : appareils de levage suivis : " + sb);
            }
        }

        static Lift FindLift(string key)
        {
            foreach (Lift d in lifts) if (d.Key == key) return d;
            return null;
        }

        static bool Raised(Lift d) { return d.Level != null && d.Level.Value > d.Rest + 0.01f; }

        static void PollLifts(float now)
        {
            bool remote = Session.RemoteCount > 0;
            foreach (Lift d in lifts)
            {
                if (d.Ctrl == null) continue;
                float v = d.Level.Value;
                if (v < d.Rest) d.Rest = v;
                if (Mathf.Abs(v - d.Last) > 1e-4f)
                {
                    // Geste du joueur d'ici (un message recu met Last a jour) : la main est a nous.
                    if (now >= d.ActiveUntil) Log.Info("atelier : " + KindName[d.Kind] + " " + d.Key + " manoeuvre ici (" + d.Last.ToString("F3") + " -> " + v.ToString("F3") + ")");
                    d.Last = v; d.ActiveUntil = now + 2.5f; d.Final = true; d.Held = false; d.By = Session.LocalId;
                }
                if (d.Open != null && d.Open.Value != d.OpenLast)
                {
                    d.OpenLast = d.Open.Value;
                    Log.Info("atelier : cric " + d.Key + (d.OpenLast ? " deplie" : " replie") + " ici");
                    if (remote) Session.SendAll(new NetWriter(Msg.Garage).U8(Session.LocalId).U8(T_FOLD | RELIABLE).Str(d.Key).Bool(d.OpenLast), true);
                }
                if (!remote) { d.Final = false; continue; }
                if (now < d.ActiveUntil) SendLift(d, false);
                else if (d.Final) { d.Final = false; SendLift(d, true); }
            }
        }

        static void SendLift(Lift d, bool final)
        {
            var w = new NetWriter(Msg.Garage).U8(Session.LocalId).U8(T_LIFT | (final ? RELIABLE : 0)).Str(d.Key).F32(d.Level.Value).U8(d.Moved.Length);
            foreach (Transform t in d.Moved)
            {
                if (t != null) w.Vec(t.localPosition).Quat(t.localRotation);
                else w.Vec(Vector3.zero).Quat(Quaternion.identity);
            }
            Session.SendAll(w, final);
        }

        static void OnLift(int who, NetReader r)
        {
            string key = r.Str();
            float lv = r.F32();
            int n = r.U8();
            var pos = new Vector3[n]; var rot = new Quaternion[n];
            for (int i = 0; i < n; i++) { pos[i] = r.Vec(); rot[i] = r.Quat(); }
            Lift d = FindLift(key);
            if (d == null) { Warn("appareil " + key + " introuvable ici"); return; }
            float now = Time.realtimeSinceStartup;
            if (now < d.ActiveUntil && d.By == Session.LocalId) Log.Info("atelier : " + KindName[d.Kind] + " " + d.Key + " repris par #" + who);
            d.Level.Value = lv; d.Last = lv; d.ActiveUntil = 0; d.Final = false; d.By = who;
            if (n == d.Moved.Length)
            {
                for (int i = 0; i < n; i++) { d.HPos[i] = pos[i]; d.HRot[i] = rot[i]; }
                d.Held = true; d.HeldUntil = now + 3f;
            }
            if (d.Kind == K_CRIC) LiftBody(d, lv > d.Rest + 0.005f);
        }

        // Plateau du cric de voiture leve : corps cinematique (le jeu en ajoute un des que le cric monte, et le detruit
        // en bas), sinon le plateau deplace sous la voiture ne la pousse pas proprement.
        static void LiftBody(Lift d, bool up)
        {
            if (d.Point == null) return;
            Rigidbody rb = d.Point.GetComponent<Rigidbody>();
            if (up && rb == null)
            {
                rb = d.Point.gameObject.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                d.Added = rb;
            }
            else if (!up && rb != null && ReferenceEquals(rb, d.Added))
            {
                Object.Destroy(rb);
                d.Added = null;
            }
        }

        static void OnFold(int who, NetReader r)
        {
            string key = r.Str();
            bool open = r.Bool();
            Lift d = FindLift(key);
            if (d == null || d.Fold == null || d.Open == null) { Warn("cric " + key + " (repli) introuvable ici"); return; }
            d.OpenLast = open;
            if (d.Open.Value == open) return;
            // Comme le clic : "Bool test" retourne Open (BoolFlip), joue le son et l'animation, active le Trigger.
            Replay.Depth++;
            try
            {
                if (d.Fold.Fsm.GetState("Bool test") != null) { d.Open.Value = !open; Game.SetState(d.Fold, "Bool test"); }
                else { d.Open.Value = open; Game.SetState(d.Fold, open ? "Open" : "Close"); }
            }
            finally { Replay.Depth--; }
            d.OpenLast = d.Open.Value;
            Log.Info("atelier : cric " + key + (open ? " deplie" : " replie") + " par #" + who + " -> " + d.Fold.ActiveStateName);
        }

        // ---------------------------------------------------------------- voiture sur un appareil leve
        static readonly Dictionary<int, float> lifted = new Dictionary<int, float>();     // numero reseau -> jusqu'a quand
        static readonly Dictionary<int, float> corrected = new Dictionary<int, float>();  // dernier recalage ici
        static int correctedCount;

        static void SendLiftedCars(float now)
        {
            if (Session.RemoteCount == 0 || VehicleSync.Count == 0) return;
            foreach (Lift d in lifts)
            {
                if (d.Point == null || !Raised(d) || !d.Point.gameObject.activeInHierarchy) continue;
                Vector3 at = d.Point.position;
                for (int i = 0; i < VehicleSync.Count; i++)
                {
                    Rigidbody b = VehicleSync.CarBody(i);
                    if (b == null || !b.gameObject.activeInHierarchy) continue;
                    Vector3 dv = b.position - at;
                    if (dv.sqrMagnitude > 3.5f * 3.5f || dv.y < -0.5f) continue;
                    if (!lifted.ContainsKey(i)) Log.Info("atelier : " + b.name + " sur le " + KindName[d.Kind] + " " + d.Key);
                    lifted[i] = now + 4f;
                }
            }
            if (lifted.Count == 0) return;
            var gone = new List<int>();
            NetWriter w = null;
            int n = 0;
            var list = new List<int>();
            foreach (KeyValuePair<int, float> kv in lifted)
            {
                if (now > kv.Value) { gone.Add(kv.Key); continue; }
                if (VehicleSync.Authority(kv.Key) != Session.LocalId || VehicleSync.IsCopy(kv.Key)) continue;
                if (VehicleSync.CarBody(kv.Key) != null) list.Add(kv.Key);
            }
            foreach (int i in gone) lifted.Remove(i);
            if (list.Count == 0) return;
            w = new NetWriter(Msg.Garage).U8(Session.LocalId).U8(T_CAR).U8(System.Math.Min(list.Count, 20));
            foreach (int i in list)
            {
                if (n++ >= 20) break;
                Rigidbody b = VehicleSync.CarBody(i);
                w.U8(i).Vec(b.position).Quat(b.rotation);
            }
            Session.SendAll(w, false);
        }

        static void OnCar(int who, NetReader r)
        {
            int n = r.U8();
            float now = Time.realtimeSinceStartup;
            for (int k = 0; k < n; k++)
            {
                int idx = r.U8();
                Vector3 pos = r.Vec();
                Quaternion rot = r.Quat();
                lifted[idx] = now + 4f;   // (si l'autorite change, le nouveau prend le relais)
                if (VehicleSync.DrivenHere(idx) || VehicleSync.IsCopy(idx)) continue;
                Rigidbody b = VehicleSync.CarBody(idx);
                if (b == null || b.isKinematic) continue;
                float dist = (b.position - pos).magnitude, ang = Quaternion.Angle(b.rotation, rot);
                if (dist < 0.04f && ang < 1.5f) continue;
                float last;
                if (corrected.TryGetValue(idx, out last) && now - last < 0.5f) continue;
                corrected[idx] = now;
                // (pose de la voiture seule : ses pieces suivent son transform ; un objet pose dedans glisse d'autant)
                Transform t = b.transform;
                t.position = pos;
                t.rotation = rot;
                b.velocity = Vector3.zero;
                b.angularVelocity = Vector3.zero;
                if (++correctedCount <= 20 || correctedCount % 50 == 0)
                    Log.Info("atelier : " + b.name + " recalee sur l'appareil (#" + who + ", ecart " + dist.ToString("F2") + " m, " + ang.ToString("F1") + " deg)");
            }
        }

        // ================================================================ chargeur avant (KEKMET)
        class Loader
        {
            public string Key; public PlayMakerFSM F; public FsmFloat Rot; public Transform Tip;
            public float Last, ActiveUntil; public bool Final;
            public bool Held; public float HeldUntil; public Quaternion HRot;
        }
        static readonly List<Loader> loaders = new List<Loader>();
        static readonly HashSet<PlayMakerFSM> loaderKnown = new HashSet<PlayMakerFSM>();

        static void ScanLoaders()
        {
            int added = 0;
            for (int ci = 0; ci < VehicleSync.LocalCount; ci++)
            {
                Rigidbody rb = VehicleSync.LocalBody(ci);
                if (rb == null) continue;
                foreach (PlayMakerFSM f in rb.GetComponentsInChildren<PlayMakerFSM>(true))
                {
                    if (f.FsmName != "Use" || loaderKnown.Contains(f)) continue;
                    FsmFloat rot = f.FsmVariables.FindFsmFloat("ArmRot");
                    FsmGameObject tip = f.FsmVariables.FindFsmGameObject("FrontLoaderTip");
                    if (rot == null || tip == null || tip.Value == null) continue;
                    loaderKnown.Add(f);
                    if (!Replay.Claim(f, Module)) { Log.Warn("atelier : chargeur " + Recon.Path(f.transform) + " deja a " + Replay.Owner(f)); continue; }
                    loaders.Add(new Loader { Key = VehicleSync.LocalKey(ci) + VehicleSync.RelPath(rb.transform, f.transform), F = f, Rot = rot, Tip = tip.Value.transform, Last = rot.Value });
                    added++;
                }
            }
            if (added > 0) Log.Info("atelier : " + added + " commandes de chargeur suivies (bras et godet, " + loaders.Count + " en tout)");
        }

        static void PollLoaders(float now)
        {
            bool remote = Session.RemoteCount > 0;
            foreach (Loader l in loaders)
            {
                if (l.F == null || l.Tip == null) continue;
                float v = l.Rot.Value;
                if (Mathf.Abs(v - l.Last) > 0.01f)
                {
                    if (now >= l.ActiveUntil) Log.Info("atelier : chargeur " + l.Key + " manoeuvre ici (ArmRot " + l.Last.ToString("F1") + " -> " + v.ToString("F1") + ")");
                    l.Last = v; l.ActiveUntil = now + 1.5f; l.Final = true; l.Held = false;
                }
                if (!remote) { l.Final = false; continue; }
                bool final = now >= l.ActiveUntil;
                if (!final || l.Final)
                {
                    if (final) l.Final = false;
                    Session.SendAll(new NetWriter(Msg.Garage).U8(Session.LocalId).U8(T_LOADER | (final ? RELIABLE : 0)).Str(l.Key).F32(v).Quat(l.Tip.localRotation), final);
                }
            }
        }

        static void OnLoader(int who, NetReader r)
        {
            string key = r.Str();
            float v = r.F32();
            Quaternion q = r.Quat();
            Loader l = null;
            foreach (Loader x in loaders) if (x.Key == key) { l = x; break; }
            if (l == null || l.F == null) { Warn("chargeur " + key + " introuvable ici"); return; }
            // ArmRot recopie : l'automate d'ici (SetRotation au repos, prochain geste) part de la meme valeur.
            l.Rot.Value = v; l.Last = v; l.ActiveUntil = 0; l.Final = false;
            l.HRot = q; l.Held = true; l.HeldUntil = Time.realtimeSinceStartup + 3f;
        }

        // ================================================================ degats
        class Dent { public string Key; public Deformable D; public Vector3[] Arr, Snap; }
        class Glass { public string Key; public GameObject Coll; public bool Had; }
        class Bend { public string Key; public Transform T; public Quaternion Snap; }
        class CarDmg
        {
            public string Car; public Transform Root;
            public List<Dent> Dents = new List<Dent>(); public List<Glass> Glasses = new List<Glass>(); public List<Bend> Bends = new List<Bend>();
        }
        static readonly Dictionary<string, CarDmg> dmg = new Dictionary<string, CarDmg>();

        const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
        static readonly FieldInfo fVerts = typeof(Deformable).GetField("vertices", Priv), fBase = typeof(Deformable).GetField("baseVertices", Priv),
                                  fColors = typeof(Deformable).GetField("colors", Priv), fBaseColors = typeof(Deformable).GetField("baseColors", Priv),
                                  fMesh = typeof(Deformable).GetField("mesh", Priv);
        static readonly MethodInfo mUpdateMesh = typeof(Deformable).GetMethod("UpdateMesh", Priv);

        static string Rank(Dictionary<string, int> seen, string k)
        {
            int n; seen.TryGetValue(k, out n); seen[k] = n + 1;
            return k + "#" + n;
        }

        static void ScanDamage()
        {
            int nd = 0, ng = 0, nb = 0;
            for (int ci = 0; ci < VehicleSync.LocalCount; ci++)
            {
                Rigidbody rb = VehicleSync.LocalBody(ci);
                if (rb == null) continue;
                string car = VehicleSync.LocalKey(ci);
                CarDmg old;
                dmg.TryGetValue(car, out old);
                if (old != null && old.Root != rb.transform) old = null;   // corps recree
                var c = new CarDmg { Car = car, Root = rb.transform };
                var seen = new Dictionary<string, int>();
                foreach (Deformable d in rb.GetComponentsInChildren<Deformable>(true))
                {
                    string key = Rank(seen, "d" + VehicleSync.RelPath(rb.transform, d.transform));
                    Dent o = null;
                    if (old != null) foreach (Dent x in old.Dents) if (x.Key == key && x.D == d) { o = x; break; }
                    c.Dents.Add(o ?? new Dent { Key = key, D = d });
                }
                foreach (PlayMakerFSM f in rb.GetComponentsInChildren<PlayMakerFSM>(true))
                {
                    if (f.FsmName != "Data") continue;
                    GameObject coll;
                    try
                    {
                        if (f.Fsm.GetState("Break glass") == null || f.Fsm.GetState("Assemble") == null) continue;
                        FsmGameObject cv = f.FsmVariables.FindFsmGameObject("Collider");
                        coll = cv != null ? cv.Value : null;
                    }
                    catch { continue; }
                    if (coll == null) continue;
                    string key = Rank(seen, "g" + VehicleSync.RelPath(rb.transform, f.transform));
                    Glass o = null;
                    if (old != null) foreach (Glass x in old.Glasses) if (x.Key == key && x.Coll == coll) { o = x; break; }
                    c.Glasses.Add(o ?? new Glass { Key = key, Coll = coll, Had = coll.GetComponent<FixedJoint>() != null });
                }
                foreach (Transform t in rb.GetComponentsInChildren<Transform>(true))
                {
                    if (!t.name.Contains("DamagePivot")) continue;
                    string key = Rank(seen, "b" + VehicleSync.RelPath(rb.transform, t));
                    Bend o = null;
                    if (old != null) foreach (Bend x in old.Bends) if (x.Key == key && x.T == t) { o = x; break; }
                    c.Bends.Add(o ?? new Bend { Key = key, T = t, Snap = t.localRotation });
                }
                dmg[car] = c;
                nd += c.Dents.Count; ng += c.Glasses.Count; nb += c.Bends.Count;
            }
            if (!dmgLogged || nd + ng + nb != dmgLast)
            {
                dmgLogged = true; dmgLast = nd + ng + nb;
                Log.Info("atelier : degats suivis sur " + dmg.Count + " vehicules : " + nd + " toles deformables, " + ng + " vitres, " + nb + " pivots de train");
            }
        }
        static bool dmgLogged;
        static int dmgLast;

        static readonly List<CarDmg> dmgList = new List<CarDmg>();

        static void PollDamage()
        {
            bool remote = Session.RemoteCount > 0 && fVerts != null && fBase != null;
            int budget = 12;   // messages de tole par passage (le reste part au suivant)
            if (dmgList.Count != dmg.Count || dmgIdx == 0) { dmgList.Clear(); dmgList.AddRange(dmg.Values); }
            if (dmgList.Count == 0) return;
            if (dmgIdx >= dmgList.Count) dmgIdx = 0;
            CarDmg c = dmgList[dmgIdx++];
            if (c.Root == null) return;
            foreach (Glass g in c.Glasses)
            {
                bool has = g.Coll != null && g.Coll.GetComponent<FixedJoint>() != null;
                if (g.Had && !has)
                {
                    Log.Info("atelier : vitre " + c.Car + g.Key + " cassee ici");
                    if (Session.RemoteCount > 0) Session.SendAll(new NetWriter(Msg.Garage).U8(Session.LocalId).U8(T_GLASS | RELIABLE).Str(c.Car).Str(g.Key), true);
                }
                g.Had = has;
            }
            bool copy = VehicleSync.RemotelyDriven(c.Root);
            foreach (Bend b in c.Bends)
            {
                if (b.T == null || Quaternion.Angle(b.T.localRotation, b.Snap) < 0.25f) continue;
                if (copy) { b.T.localRotation = b.Snap; continue; }
                b.Snap = b.T.localRotation;
                Log.Info("atelier : train " + c.Car + b.Key + " tordu ici (" + b.Snap.eulerAngles.ToString("F1") + ")");
                if (Session.RemoteCount > 0) Session.SendAll(new NetWriter(Msg.Garage).U8(Session.LocalId).U8(T_BEND | RELIABLE).Str(c.Car).Str(b.Key).Quat(b.Snap), true);
            }
            foreach (Dent d in c.Dents) budget = copy ? UndoDent(c, d) : PollDent(c, d, remote, budget);
        }

        // Copie conduite ailleurs : sommets bouges ici remis a leur derniere valeur connue (recue ou d'avant la copie).
        static int UndoDent(CarDmg c, Dent d)
        {
            if (d.D == null || fVerts == null || fBase == null) return 0;
            var v = fVerts.GetValue(d.D) as Vector3[];
            var bs = fBase.GetValue(d.D) as Vector3[];
            if (v == null || bs == null || bs.Length != v.Length) return 0;
            if (d.Snap == null || !ReferenceEquals(d.Arr, v) || d.Snap.Length != v.Length) { d.Arr = v; d.Snap = (Vector3[])v.Clone(); return 0; }
            var cols = fColors != null ? fColors.GetValue(d.D) as Color32[] : null;
            var bcols = fBaseColors != null ? fBaseColors.GetValue(d.D) as Color32[] : null;
            bool tint = cols != null && bcols != null && cols.Length == v.Length && bcols.Length == v.Length && d.D.MaxVertexMov > 0f;
            int n = 0;
            for (int i = 0; i < v.Length; i++)
            {
                if ((v[i] - d.Snap[i]).sqrMagnitude <= 1e-8f) continue;
                v[i] = d.Snap[i];
                if (tint) cols[i] = Color.Lerp(bcols[i], d.D.DeformedVertexColor, (v[i] - bs[i]).magnitude / d.D.MaxVertexMov);
                n++;
            }
            if (n == 0) return 0;
            try { if (mUpdateMesh != null) mUpdateMesh.Invoke(d.D, null); } catch (System.Exception) { }
            if (undoLogs++ < 10) Log.Info("atelier : tole " + c.Car + d.Key + " cabossee sur la copie (" + n + " sommets) : remise comme chez le conducteur");
            return 0;
        }
        static int undoLogs;

        static int PollDent(CarDmg c, Dent d, bool remote, int budget)
        {
            if (d.D == null || fVerts == null) return budget;
            var v = fVerts.GetValue(d.D) as Vector3[];
            var bs = fBase != null ? fBase.GetValue(d.D) as Vector3[] : null;
            if (v == null || bs == null || bs.Length != v.Length) return budget;
            // Maillage recharge (autre tableau) ou premier passage : nouvelle reference, rien d'envoye.
            if (d.Snap == null || !ReferenceEquals(d.Arr, v) || d.Snap.Length != v.Length) { d.Arr = v; d.Snap = (Vector3[])v.Clone(); return budget; }
            List<int> changed = null;
            for (int i = 0; i < v.Length; i++)
                if ((v[i] - d.Snap[i]).sqrMagnitude > 1e-8f) { if (changed == null) changed = new List<int>(); changed.Add(i); }
            if (changed == null) return budget;
            if (!remote) { foreach (int i in changed) d.Snap[i] = v[i]; return budget; }
            int head = 16 + System.Text.Encoding.UTF8.GetByteCount(c.Car) + System.Text.Encoding.UTF8.GetByteCount(d.Key);
            int per = Mathf.Max(10, (1000 - head) / 8);
            int k = 0, sent = 0;
            while (k < changed.Count && budget > 0)
            {
                int n = Mathf.Min(per, changed.Count - k);
                var w = new NetWriter(Msg.Garage).U8(Session.LocalId).U8(T_DENT | RELIABLE).Str(c.Car).Str(d.Key).U16(v.Length).U8(n);
                for (int j = 0; j < n; j++)
                {
                    int i = changed[k + j];
                    Vector3 off = v[i] - bs[i];
                    w.U16(i).U16(Mm(off.x)).U16(Mm(off.y)).U16(Mm(off.z));
                    d.Snap[i] = v[i];
                }
                Session.SendAll(w, true);
                k += n; sent += n; budget--;
            }
            Log.Info("atelier : tole " + c.Car + d.Key + " deformee ici, " + sent + " sommets envoyes" + (k < changed.Count ? " (" + (changed.Count - k) + " au prochain passage)" : ""));
            return budget;
        }

        // Decalage en millimetres sur 16 bits signes (+-32 m).
        static int Mm(float m) { return (ushort)(short)Mathf.Clamp(Mathf.RoundToInt(m * 1000f), -32767, 32767); }

        static CarDmg DmgOf(string car)
        {
            CarDmg c;
            return dmg.TryGetValue(car, out c) && c.Root != null ? c : null;
        }

        static void OnGlass(int who, NetReader r)
        {
            string car = r.Str(), key = r.Str();
            CarDmg c = DmgOf(car);
            Glass g = null;
            if (c != null) foreach (Glass x in c.Glasses) if (x.Key == key) { g = x; break; }
            if (g == null) { Warn("vitre " + car + key + " introuvable ici"); return; }
            g.Had = false;
            FixedJoint fj = g.Coll != null ? g.Coll.GetComponent<FixedJoint>() : null;
            // Comme le choc chez l'autre : l'attache disparait, 'Assemble' (HasComponent) casse la vitre a l'image suivante.
            if (fj != null) Object.Destroy(fj);
            Log.Info("atelier : vitre " + car + key + " cassee par #" + who + (fj != null ? "" : " (deja cassee ici)"));
        }

        static void OnBend(int who, NetReader r)
        {
            string car = r.Str(), key = r.Str();
            Quaternion q = r.Quat();
            CarDmg c = DmgOf(car);
            Bend b = null;
            if (c != null) foreach (Bend x in c.Bends) if (x.Key == key) { b = x; break; }
            if (b == null || b.T == null) { Warn("train " + car + key + " introuvable ici"); return; }
            b.T.localRotation = q;
            b.Snap = q;
            Log.Info("atelier : train " + car + key + " tordu par #" + who + " (" + q.eulerAngles.ToString("F1") + ")");
        }

        static void OnDent(int who, NetReader r)
        {
            string car = r.Str(), key = r.Str();
            int count = r.U16(), n = r.U8();
            var idx = new int[n]; var off = new Vector3[n];
            for (int j = 0; j < n; j++)
            {
                idx[j] = r.U16();
                off[j] = new Vector3((short)r.U16(), (short)r.U16(), (short)r.U16()) * 0.001f;
            }
            CarDmg c = DmgOf(car);
            Dent d = null;
            if (c != null) foreach (Dent x in c.Dents) if (x.Key == key) { d = x; break; }
            if (d == null || d.D == null || fVerts == null || fBase == null) { Warn("tole " + car + key + " introuvable ici"); return; }
            var v = fVerts.GetValue(d.D) as Vector3[];
            var bs = fBase.GetValue(d.D) as Vector3[];
            if (v == null || bs == null || v.Length != count || bs.Length != count || fMesh == null || fMesh.GetValue(d.D) == null)
            { Warn("tole " + car + key + " : maillage different ici (" + (v != null ? v.Length : -1) + " sommets, recu " + count + ")"); return; }
            if (d.Snap == null || !ReferenceEquals(d.Arr, v) || d.Snap.Length != v.Length) { d.Arr = v; d.Snap = (Vector3[])v.Clone(); }
            var cols = fColors != null ? fColors.GetValue(d.D) as Color32[] : null;
            var bcols = fBaseColors != null ? fBaseColors.GetValue(d.D) as Color32[] : null;
            bool tint = cols != null && bcols != null && cols.Length == count && bcols.Length == count && d.D.MaxVertexMov > 0f;
            for (int j = 0; j < n; j++)
            {
                int i = idx[j];
                if (i < 0 || i >= count) continue;
                v[i] = bs[i] + off[j];
                d.Snap[i] = v[i];
                if (tint) cols[i] = Color.Lerp(bcols[i], d.D.DeformedVertexColor, off[j].magnitude / d.D.MaxVertexMov);
            }
            try { if (mUpdateMesh != null) mUpdateMesh.Invoke(d.D, null); }
            catch (System.Exception e) { Warn("tole " + car + key + " : maillage pas mis a jour (" + e.GetType().Name + ")"); }
            dentsIn += n;
            if (++dentMsgs <= 10 || dentMsgs % 50 == 0) Log.Info("atelier : tole " + car + key + " deformee par #" + who + " (" + n + " sommets, " + dentsIn + " recus en tout)");
        }
        static int dentsIn, dentMsgs;

        static void Warn(string s)
        {
            if (Time.realtimeSinceStartup < nextWarn) return;
            nextWarn = Time.realtimeSinceStartup + 5f;
            Log.Warn("atelier : " + s);
        }

        // ================================================================ messages
        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            int type = r.U8();
            if (Session.IsHost) Session.Broadcast(new NetWriter(Msg.Garage).U8(who).U8(type).Raw(r.Rest()), (type & RELIABLE) != 0, who);
            if (nextScan < 0) return;
            switch (type & 0x7F)
            {
                case T_LIFT: OnLift(who, r); break;
                case T_FOLD: OnFold(who, r); break;
                case T_CAR: OnCar(who, r); break;
                case T_GLASS: OnGlass(who, r); break;
                case T_DENT: OnDent(who, r); break;
                case T_BEND: OnBend(who, r); break;
                case T_LOADER: OnLoader(who, r); break;
                default: Warn("message " + type + " inconnu"); break;
            }
        }

        // ================================================================ essais
        // Chaque mode : T = l'autre joueur en partie depuis 15 s (Jobs.OtherInGame) ; l'hote agit comme le joueur
        // (etats des automates du jeu, actions de saisie coupees le temps du geste) ; les deux notent leur etat
        // toutes les 2 s de T-2 a T+40 : "autotest : <mode> (hote|invite) ...".
        // [Test] Autotest=cric (TestPos=-17,0.5,3 : garage de la maison, EQUIPMENTS) : T, cric rouleur pompe 8 fois
        //   (Use "Up" toutes les 0,6 s) ; T+7 cric de voiture deplie (Fold "Bool test") ; T+9 leve 5 fois ("On terrain"
        //   -> Check rigidbody -> Up, toutes les 0,8 s) ; T+15 palan monte (Usage "Up" tenu 2 s) ; T+22 cric rouleur
        //   redescendu ("Down"). Attendu chez l'invite : memes niveaux (Y, Angle) et memes poses locales des plateaux
        //   (ecart < 1 cm), "deplie par #0", corps cinematique sur le plateau du cric de voiture.
        // [Test] Autotest=pont (TestPos=1561,5.6,733 : devant la commande du pont Fleetari) : T, l'hote pose
        //   [Test] PontVoiture (BACHGLOTZ par defaut) sur le plateau ; T+6 monte (Lift up tenu, jusqu'a la butee ou
        //   6 s) ; T+24 redescend (Lift down). Attendu : PlatformPos et hauteur du plateau egaux, la voiture levee chez
        //   l'invite aussi (ecart vertical < 5 cm, "recalee sur l'appareil" au besoin).
        // [Test] Autotest=degats (TestPos=1933,5,-419 : a cote de la CORRIS ; [Test] DegatsVoiture=CORRIS) : T, l'hote
        //   casse le pare-brise (attache du collider detruite, comme un choc) ; T+4 enfonce la tole (sommets a moins de
        //   0,4 m d'un point, 6 cm vers l'interieur, UpdateMesh) ; T+8 tord le premier pivot de train (+4 deg).
        //   Attendu chez l'invite : "vitre ... cassee par #0", attache absente et 'Data' en "Break glass" ; meme somme
        //   des enfoncements (mm) ; meme rotation du pivot.
        // [Test] Autotest=chargeur (TestPos=52,0,-86 : a cote du KEKMET) : T, l'hote abaisse ArmRot du bras de 1 toutes
        //   les 0,1 s pendant 3 s (etat "Set Position 2" : SetRotation du jeu) ; T+5 le godet (+1, "Set Position") ;
        //   T+10 retour. Attendu : ArmRot egaux, rotations des pieces a moins de 1 deg.
        static int tStep;
        static float tAt, tLog, tNext;
        static readonly List<FsmStateAction> testOff = new List<FsmStateAction>();
        static PlayMakerFSM testFsm;
        static string testState, testBack;
        static float testUntil, testStop = float.NaN;
        static int testCount;

        public static void Test(string mode, float t)
        {
            if (mode != "cric" && mode != "pont" && mode != "degats" && mode != "chargeur") return;
            float now = Time.realtimeSinceStartup;
            bool host = Session.IsHost;
            if (testFsm != null && (now > testUntil || testFsm.ActiveStateName != testState
                                     || (!float.IsNaN(testStop) && Lvl(testFsm) >= testStop))) Undrive();
            if (tStep == 0)
            {
                if (!Jobs.OtherInGame(15f)) return;
                tStep = 1; tAt = now; tNext = 0; testCount = 0;
                Log.Info("autotest : " + mode + " (" + (host ? "hote" : "invite") + "), debut : " + TestState(mode));
                if (host) DumpFor(mode);
            }
            float dt = now - tAt;
            if (dt < 40f && now >= tLog) { tLog = now + 2f; Log.Info("autotest : " + mode + " (" + (host ? "hote" : "invite") + ") t+" + dt.ToString("F0") + " " + TestState(mode)); }
            if (!host) return;
            if (mode == "cric") TestCric(dt, now);
            else if (mode == "pont") TestPont(dt);
            else if (mode == "degats") TestDegats(dt);
            else TestChargeur(dt, now);
        }

        // Parametres des actions qu'on suppose (cible des iTween/SetPosition/SetRotation, objet qui recoit le corps du
        // cric...) : notes au debut de l'essai pour verification.
        static void DumpFor(string mode)
        {
            var l = new List<KeyValuePair<PlayMakerFSM, string>>();
            foreach (Lift d in lifts)
            {
                if ((mode == "pont") != (d.Kind == K_PONT) || mode == "degats" || mode == "chargeur") continue;
                string[] st = d.Kind == K_CRIC ? new[] { "Up", "Check rigidbody", "Add rigidbody", "State 1" } : d.Kind == K_CRICR ? new[] { "Up", "Down" }
                            : d.Kind == K_PONT ? new[] { "Lift up", "Wait player" } : new[] { "Up" };
                foreach (string s in st) l.Add(new KeyValuePair<PlayMakerFSM, string>(d.Ctrl, s));
                if (d.Fold != null) l.Add(new KeyValuePair<PlayMakerFSM, string>(d.Fold, "Bool test"));
                Transform lift = d.Point;
                PlayMakerFSM mv = lift != null ? Game.FsmOn(lift.gameObject, "Movement") : null;
                if (mv != null) l.Add(new KeyValuePair<PlayMakerFSM, string>(mv, "State 1"));
            }
            if (mode == "chargeur")
                foreach (Loader x in loaders)
                    foreach (string s in new[] { "INCREASE", "INCREASE 2", "Set Position", "Set Position 2", "Wait player", "Wait player 2" })
                        if (x.F != null && x.F.Fsm.GetState(s) != null) l.Add(new KeyValuePair<PlayMakerFSM, string>(x.F, s));
            foreach (KeyValuePair<PlayMakerFSM, string> kv in l)
                Log.Info("autotest : " + mode + ", actions " + kv.Key.gameObject.name + "::" + kv.Key.FsmName + " " + Jobs.DumpActions(kv.Key, kv.Value));
        }

        static float Lvl(PlayMakerFSM f)
        {
            foreach (Lift d in lifts) if (d.Ctrl == f) return d.Level.Value;
            return float.NaN;
        }

        static Lift TestLift(int kind)
        {
            foreach (Lift d in lifts) if (d.Kind == kind && d.Root != null && d.Root.gameObject.activeInHierarchy) return d;
            foreach (Lift d in lifts) if (d.Kind == kind) return d;
            return null;
        }

        // Comme un bouton tenu : l'etat 'state' sans ses actions de saisie (souris, distance au joueur et le test qui
        // suit) ; rendu a 'back' apres 'secs' (ou au niveau 'stop').
        static string Drive(PlayMakerFSM f, string state, string back, float secs, float stop)
        {
            Undrive();
            if (f == null) return "pas d'automate";
            FsmState s = f.Fsm.GetState(state);
            if (s == null) return "pas d'etat " + state + " dans " + f.FsmName;
            bool afterDist = false;
            foreach (FsmStateAction a in s.Actions)
            {
                if (a == null || !a.Enabled || a is ModHook) continue;
                string tn = a.GetType().Name;
                if (tn == "GetDistance") afterDist = true;
                bool input = tn.StartsWith("GetMouse") || tn.StartsWith("GetButton") || tn.StartsWith("GetKey") || tn.StartsWith("MousePick") || tn == "GetDistance";
                if (input || (afterDist && tn == "FloatCompare")) { a.Enabled = false; testOff.Add(a); }
            }
            testFsm = f; testState = state; testBack = back; testUntil = Time.realtimeSinceStartup + secs; testStop = stop;
            Game.SetState(f, state);
            return f.FsmName + " de " + f.gameObject.name + " -> " + f.ActiveStateName + " (" + testOff.Count + " actions de saisie coupees)";
        }

        static void Undrive()
        {
            if (testFsm != null && testFsm.ActiveStateName == testState && testBack != null && testFsm.Fsm.GetState(testBack) != null) Game.SetState(testFsm, testBack);
            foreach (FsmStateAction a in testOff) a.Enabled = true;
            testOff.Clear();
            testFsm = null; testStop = float.NaN;
        }

        static void TestCric(float dt, float now)
        {
            Lift fj = TestLift(K_CRICR), cj = TestLift(K_CRIC), ho = TestLift(K_PALAN);
            if (tStep == 1 && now >= tNext && fj != null)
            {
                tNext = now + 0.6f;
                Game.SetState(fj.Ctrl, "Up");   // un coup de levier
                if (++testCount >= 8) { tStep = 2; Log.Info("autotest : cric, rouleur pompe 8 fois : Y " + fj.Level.Value.ToString("F3")); }
            }
            if (tStep == 1 && fj == null) { tStep = 2; Log.Info("autotest : cric, pas de cric rouleur ici"); }
            if (tStep == 2 && dt > 7f)
            {
                tStep = 3; testCount = 0;
                if (cj == null || cj.Fold == null) Log.Info("autotest : cric, pas de cric de voiture (ou sans 'Fold')");
                else if (cj.Open != null && cj.Open.Value) Log.Info("autotest : cric, cric de voiture deja deplie");
                else { Game.SetState(cj.Fold, "Bool test"); Log.Info("autotest : cric, cric de voiture deplie -> " + cj.Fold.ActiveStateName); }
            }
            if (tStep == 3 && dt > 9f && now >= tNext)
            {
                tNext = now + 0.8f;
                if (cj != null && cj.Ctrl.gameObject.activeInHierarchy)
                {
                    Game.SetState(cj.Ctrl, "On terrain");   // clic LIFT UP : verifie le sol, corps cinematique, Up
                    Log.Info("autotest : cric, cric de voiture leve -> " + cj.Ctrl.ActiveStateName + ", Y " + cj.Level.Value.ToString("F3"));
                }
                if (++testCount >= 5 || cj == null) tStep = 4;
            }
            if (tStep == 4 && dt > 15f) { tStep = 5; Log.Info("autotest : cric, palan : " + (ho != null ? Drive(ho.Ctrl, "Up", "Wait player 2", 2f, float.NaN) : "absent")); }
            if (tStep == 5 && dt > 22f) { tStep = 6; if (fj != null) Game.SetState(fj.Ctrl, "Down"); Log.Info("autotest : cric, rouleur redescendu"); }
        }

        static void TestPont(float dt)
        {
            Lift p = TestLift(K_PONT);
            if (tStep == 1)
            {
                tStep = 2;
                if (p == null || p.Point == null) { Log.Info("autotest : pont, pas de pont ici"); return; }
                string name = Config.Get("Test", "PontVoiture", "BACHGLOTZ");
                Rigidbody car = VehicleSync.Body(name);
                if (car == null) { Log.Info("autotest : pont, voiture " + name + " inconnue"); return; }
                // Voiture garee posee sur le plateau (l'hote en a l'autorite ; VehicleSync la recale chez l'invite) : en
                // travers des deux bras (Platform/Coll), au milieu.
                Transform ct = car.transform;
                Vector3 mid = p.Point.position, fwd = p.Point.forward;
                var arms = new List<Transform>();
                foreach (Transform ch in p.Point) if (ch.name == "Coll") arms.Add(ch);
                if (arms.Count >= 2)
                {
                    mid = (arms[0].position + arms[1].position) * 0.5f;
                    Vector3 side = arms[1].position - arms[0].position; side.y = 0f;
                    if (side.sqrMagnitude > 1e-4f) fwd = Vector3.Cross(Vector3.up, side.normalized);
                }
                fwd.y = 0f;
                ct.position = mid + Vector3.up * 1.2f;
                ct.rotation = Quaternion.LookRotation(fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward) * Quaternion.Euler(0f, Config.GetInt("Test", "PontLacet", 0), 0f);
                car.velocity = Vector3.zero; car.angularVelocity = Vector3.zero;
                Log.Info("autotest : pont, " + name + " posee sur le plateau en " + ct.position.ToString("F1"));
            }
            if (p == null) return;
            if (tStep == 2 && dt > 6f) { tStep = 3; Log.Info("autotest : pont, monte : " + Drive(p.Ctrl, "Lift up", "Wait player", 6f, p.Rest + 1.2f)); }
            if (tStep == 3 && dt > 24f) { tStep = 4; Log.Info("autotest : pont, descend : " + Drive(p.Ctrl, "Lift down", "Wait player", 7f, float.NaN)); }
        }

        static CarDmg TestCar()
        {
            string name = Config.Get("Test", "DegatsVoiture", "CORRIS");
            foreach (CarDmg c in dmg.Values) if (c.Car == name || c.Car.StartsWith(name)) return c;
            return null;
        }

        static void TestDegats(float dt)
        {
            CarDmg c = TestCar();
            if (tStep == 1)
            {
                tStep = 2;
                Glass g = c != null && c.Glasses.Count > 0 ? c.Glasses[0] : null;
                FixedJoint fj = g != null && g.Coll != null ? g.Coll.GetComponent<FixedJoint>() : null;
                if (fj != null) Object.Destroy(fj);   // comme le choc : l'attache casse, 'Assemble' casse la vitre
                Log.Info("autotest : degats, " + (g == null ? "pas de vitre suivie" : fj == null ? "vitre " + g.Key + " deja cassee" : "vitre " + g.Key + " cassee"));
            }
            if (tStep == 2 && dt > 4f)
            {
                tStep = 3;
                Dent d = null;
                if (c != null && fVerts != null && fMesh != null && mUpdateMesh != null)
                    foreach (Dent x in c.Dents) if (x.D != null && fVerts.GetValue(x.D) is Vector3[] && fMesh.GetValue(x.D) != null) { d = x; break; }
                if (d == null) { Log.Info("autotest : degats, pas de tole deformable"); return; }
                var v = (Vector3[])fVerts.GetValue(d.D);
                Vector3 at = v[v.Length / 2];
                int n = 0;
                for (int i = 0; i < v.Length; i++)
                    if ((v[i] - at).sqrMagnitude < 0.16f) { v[i] -= (v[i].sqrMagnitude > 1e-6f ? v[i].normalized : Vector3.up) * 0.06f; n++; }
                try { mUpdateMesh.Invoke(d.D, null); } catch (System.Exception e) { Log.Info("autotest : degats, UpdateMesh " + e.GetType().Name); }
                Log.Info("autotest : degats, tole " + d.Key + " enfoncee : " + n + " sommets");
            }
            if (tStep == 3 && dt > 8f)
            {
                tStep = 4;
                Bend b = c != null && c.Bends.Count > 0 ? c.Bends[0] : null;
                if (b == null || b.T == null) { Log.Info("autotest : degats, pas de pivot de train"); return; }
                b.T.localRotation = b.T.localRotation * Quaternion.Euler(0f, 4f, 0f);   // comme 'Bend' de Calculations (SetRotation)
                Log.Info("autotest : degats, pivot " + b.Key + " tordu : " + b.T.localEulerAngles.ToString("F1"));
            }
        }

        static void TestChargeur(float dt, float now)
        {
            Loader arm = null, bucket = null;
            foreach (Loader l in loaders)
            {
                if (l.F == null) continue;
                if (l.F.gameObject.name == "FrontHydArm") arm = l; else if (l.F.gameObject.name == "FrontHydLoader") bucket = l;
            }
            if (tStep == 1) { tStep = 2; if (arm == null && bucket == null) Log.Info("autotest : chargeur, pas de chargeur suivi (" + loaders.Count + ")"); }
            if (now < tNext) return;
            tNext = now + 0.1f;
            // Comme le levier tenu : ArmRot change pas a pas, SetRotation du jeu (etat de pose au chargement).
            if (dt < 3f) Step(arm, -1f, "Set Position 2");
            else if (dt > 5f && dt < 8f) Step(bucket, 1f, "Set Position");
            else if (dt > 10f && dt < 13f) { Step(arm, 1f, "Set Position 2"); Step(bucket, -1f, "Set Position"); }
        }

        static void Step(Loader l, float d, string state)
        {
            if (l == null || l.F == null || l.F.Fsm.GetState(state) == null) return;
            l.Rot.Value += d;
            Game.SetState(l.F, state);
        }

        static string TestState(string mode)
        {
            var sb = new System.Text.StringBuilder();
            if (mode == "cric" || mode == "pont")
            {
                foreach (Lift d in lifts)
                {
                    if ((mode == "pont") != (d.Kind == K_PONT)) continue;
                    if (d.Root == null || !d.Root.gameObject.activeInHierarchy) continue;
                    sb.Append(" | ").Append(KindName[d.Kind]).Append(' ').Append(d.Key.Substring(d.Key.IndexOf(':') + 1)).Append(" niveau ").Append(d.Level.Value.ToString("F3"));
                    Transform pt = d.Point ?? (d.Moved.Length > 0 ? d.Moved[0] : null);
                    if (pt != null) sb.Append(d.Kind == K_PALAN ? " rot " + pt.localEulerAngles.ToString("F1") : " plateau " + pt.localPosition.ToString("F3"));
                    if (d.Open != null) sb.Append(" deplie ").Append(d.Open.Value);
                    if (d.Point != null && d.Kind == K_CRIC) sb.Append(" corps ").Append(d.Point.GetComponent<Rigidbody>() != null);
                    sb.Append(" etat ").Append(d.Ctrl.ActiveStateName).Append(d.Held ? " (tenu)" : "");
                }
                if (mode == "pont")
                {
                    Rigidbody car = VehicleSync.Body(Config.Get("Test", "PontVoiture", "BACHGLOTZ"));
                    if (car != null) sb.Append(" | voiture ").Append(car.position.ToString("F2")).Append(" rot ").Append(car.rotation.eulerAngles.ToString("F1"))
                                       .Append(" levee ").Append(lifted.ContainsKey(VehicleSync.CarIndex(car))).Append(" recalages ").Append(correctedCount);
                }
                return sb.Length > 0 ? sb.ToString() : "aucun appareil actif ici (" + lifts.Count + " suivis)";
            }
            if (mode == "chargeur")
            {
                foreach (Loader l in loaders)
                    if (l.F != null) sb.Append(" | ").Append(l.F.gameObject.name).Append(" ArmRot ").Append(l.Rot.Value.ToString("F1")).Append(" piece ").Append(l.Tip.localEulerAngles.ToString("F1")).Append(l.Held ? " (tenu)" : "");
                return sb.Length > 0 ? sb.ToString() : "pas de chargeur suivi";
            }
            CarDmg c = TestCar();
            if (c == null) return "voiture " + Config.Get("Test", "DegatsVoiture", "CORRIS") + " pas suivie (" + dmg.Count + ")";
            foreach (Glass g in c.Glasses)
            {
                PlayMakerFSM data = null;
                if (g.Coll != null && g.Coll.transform.parent != null) data = Game.FsmOn(g.Coll.transform.parent.gameObject, "Data");
                sb.Append(" | vitre ").Append(g.Key).Append(" attache ").Append(g.Coll != null && g.Coll.GetComponent<FixedJoint>() != null).Append(" etat ").Append(data != null ? data.ActiveStateName : "?");
            }
            foreach (Dent d in c.Dents)
            {
                var v = d.D != null && fVerts != null ? fVerts.GetValue(d.D) as Vector3[] : null;
                var bs = d.D != null && fBase != null ? fBase.GetValue(d.D) as Vector3[] : null;
                if (v == null || bs == null || v.Length != bs.Length) { sb.Append(" | tole ").Append(d.Key).Append(" pas chargee"); continue; }
                double sum = 0; int moved = 0;
                for (int i = 0; i < v.Length; i++) { float m = (v[i] - bs[i]).magnitude; if (m > 1e-4f) { moved++; sum += m; } }
                sb.Append(" | tole ").Append(d.Key).Append(' ').Append(moved).Append(" sommets, ").Append((sum * 1000).ToString("F0")).Append(" mm");
            }
            if (c.Bends.Count > 0 && c.Bends[0].T != null) sb.Append(" | pivot ").Append(c.Bends[0].Key).Append(' ').Append(c.Bends[0].T.localEulerAngles.ToString("F1"));
            return sb.ToString();
        }
    }
}
