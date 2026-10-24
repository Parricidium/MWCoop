using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Pieces de My Summer Car (vidage v.250908-04, 19/10). Pas d'ID ni de points VINP_ : autre mecanique, meme principe.
    //  - point de montage : un declencheur (SATSUMA/Body/trigger_door_left...) avec l'automate 'Assembly' : la piece tenue en main
    //    (Part), 'Wait for assembly' -ASSEMBLE-> (le clic) 'Wait' -> 'Freeze car' -> 'Set joint 2' -> 'Assemble 2' (SetParent
    //    sur son pivot, Database/.../Data.Installed) -> 'End' (le declencheur s'eteint). Au chargement le jeu passe par 'Check
    //    Assembly' (Setup), jamais par 'Wait for assembly' : seul ce passage-la est un montage du joueur.
    //  - demontage : l'automate 'Removal' de la piece, etat 'Remove part' (declencheur rallume, piece detachee). 'Mouse over'
    //    -REMOVE-> : clic du joueur ; 'Mouse off' -REMOVE-> : casse (joint rompu).
    //  - vis : 'Screw' (Stage 0..8, PartAssembled) ; TIGHTEN / UNTIGHTEN -> 'Screw 2' / 'Unscrew 2' -> 'Wait 3' / 'Wait 4' (+-1 au
    //    BoltCheck.Tightness de la piece). Une action au debut de 'Wait 3' / 'Wait 4' envoie (piece, vis, Stage) ; ailleurs on
    //    rejoue TIGHTEN / UNTIGHTEN jusqu'a ce Stage.
    // Cle d'une piece : "db:" + chemin de son entree Database (db_ThisPart de 'Removal'), sinon l'ID de son 'Use' (roues), sinon
    // son chemin a rangs a la premiere vue. Cle d'un point : cle de la piece qui le porte + chemin relatif, ou chemin complet.
    //  - points « echange » (retour de $forever1, 20/10 : toute la mecanique de CARPARTS/PartsCar -- sub frame, trains, freins,
    //    direction ; 75 points) : pas de {Parent} ni de 'Assemble 2'. Le clic mene a 'Assemble' (ou a 'Check bolts' puis
    //    'Assemble') qui allume la piece montee deja dans la voiture (SATSUMA/.../sub frame(xxxxx)), met Installed et DETRUIT la
    //    piece libre ({Part}, sans automate : sa cle est celle de Props, w:...). Rejoue : 'Assemble', avec la copie d'ici de la
    //    piece libre (Props) si elle existe, sinon sans. Demontage : 'Remove part' cree une NOUVELLE piece libre ({SpawnPart} au
    //    RemovalPoint) -- celui qui demonte lui donne une cle (Props.Adopt), envoyee avec le demontage ; l'autre la donne a la
    //    sienne. Avant : « piece w:... introuvable ici », pieces restees par terre chez l'hote, 551 vis refusees.
    public static partial class Parts
    {
        class MscPoint { public string Key; public PlayMakerFSM Fsm; public string Click; public bool Swap; }
        static readonly Dictionary<string, MscPoint> mscPoints = new Dictionary<string, MscPoint>();
        static readonly Dictionary<PlayMakerFSM, MscPoint> mscPointOf = new Dictionary<PlayMakerFSM, MscPoint>();
        static readonly HashSet<PlayMakerFSM> mscRemovals = new HashSet<PlayMakerFSM>();
        static readonly Dictionary<GameObject, string> mscFirstKey = new Dictionary<GameObject, string>();
        static readonly Dictionary<string, GameObject> mscParts = new Dictionary<string, GameObject>();
        static float mscPartsAt = -100f;

        class MscPointHook : ModHook
        {
            public override string Module { get { return "pieces"; } }
            public MscPoint P;
            public override void OnEnter()
            {
                try
                {
                    FsmState prev = P.Fsm.Fsm.PreviousActiveState;
                    if (!applying && Replay.Depth == 0 && prev != null && (prev.Name == "Wait for assembly" || P.Swap && prev.Name == "Check bolts")) MscLocalInstall(P);
                }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        class MscRemovalHook : ModHook
        {
            public override string Module { get { return "pieces"; } }
            public PlayMakerFSM F;
            public override void OnEnter()
            {
                try
                {
                    FsmState prev = F.Fsm.PreviousActiveState;
                    // (piece « echange » : pas de joint, 'Mouse off' -REMOVE-> y est le decrochage {Detach} du joueur d'ici, pas une casse)
                    if (!applying && Replay.Depth == 0)
                    {
                        bool breakoff = prev != null && prev.Name == "Mouse off" && F.FsmVariables.FindFsmGameObject("SpawnPart") == null;
                        if (breakoff && MscKeepOnCopy(F)) { Finish(); return; }
                        MscLocalRemove(F, breakoff);
                    }
                }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        class MscBoltHook : ModHook
        {
            public override string Module { get { return "pieces (boulons)"; } }
            public Bolt B;
            public override void OnEnter()
            {
                try { if (!applying && Replay.Depth == 0) MscLocalBolt(B); } catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        static void MscReset()
        {
            mscPoints.Clear(); mscPointOf.Clear(); mscRemovals.Clear(); mscFirstKey.Clear(); mscParts.Clear(); mscPartsAt = -100f; mscOut.Clear(); keptJoints.Clear();
        }

        // ---------------------------------------------------------------- cles
        public static string MscPartKey(GameObject part)
        {
            if (part == null) return "";
            PlayMakerFSM rem = Game.FsmOn(part, "Removal");
            FsmGameObject db = rem != null ? rem.FsmVariables.FindFsmGameObject("db_ThisPart") : null;
            if (db != null && db.Value != null) return "db:" + Recon.Path(db.Value.transform);
            PlayMakerFSM use = Game.FsmOn(part, "Use");
            FsmString id = use != null ? use.FsmVariables.FindFsmString("ID") : null;
            if (id != null && id.Value.Length > 0) return "id:" + id.Value;
            // Sans entree Database (guirlande de Noel...) : la cle du monde de Props, la meme chez tous (prise au chargement) ; sinon le
            // chemin a rangs, jamais pris sous PLAYER (piece en main : chemin propre a ce joueur -- « introuvable ici » chez l'autre).
            string k = Props.KnownWorldKey(part);
            if (k != null) return k;
            if (mscFirstKey.TryGetValue(part, out k)) return k;
            k = "r:" + RankPath(part.transform);
            if (!Game.UnderPlayer(part.transform)) mscFirstKey[part] = k;
            return k;
        }

        static bool IsMscPart(GameObject go) { return go != null && Game.FsmOn(go, "Removal") != null; }

        static string MscPointKey(Transform t)
        {
            string rel = t.name;
            for (Transform p = t.parent; p != null; p = p.parent)
            {
                if (IsMscPart(p.gameObject)) return MscPartKey(p.gameObject) + "/" + rel;
                rel = Link(p) + "/" + rel;
            }
            return rel;
        }

        static GameObject MscFindPart(string key)
        {
            GameObject go;
            if (mscParts.TryGetValue(key, out go) && go != null) return go;
            if (Time.realtimeSinceStartup - mscPartsAt < 1f) return null;
            mscPartsAt = Time.realtimeSinceStartup;
            mscParts.Clear();
            foreach (Object o in Game.FsmsNamed("Removal", 0.5f))
            {
                var f = o as PlayMakerFSM;
                if (f == null || f.hideFlags != HideFlags.None) continue;
                mscParts[MscPartKey(f.gameObject)] = f.gameObject;
            }
            return mscParts.TryGetValue(key, out go) ? go : null;
        }

        // ---------------------------------------------------------------- releve
        static bool IsMscPoint(PlayMakerFSM f)
        {
            if (f.FsmName != "Assembly" || f.FsmVariables.FindFsmGameObject("Part") == null || f.Fsm.GetState("Wait for assembly") == null) return false;
            return Game.RootName(f.transform) != "ITEMS";   // (piles de la radio : ItemFsms)
        }

        static bool IsMscBolt(PlayMakerFSM f)
        {
            return f.FsmName == "Screw" && f.FsmVariables.FindFsmInt("Stage") != null && f.FsmVariables.FindFsmGameObject("PartAssembled") != null
                   && f.Fsm.GetState("Wait 3") != null && f.Fsm.GetState("Wait 4") != null;
        }

        // Point « echange » : ni {Parent} ni 'Assemble 2', un etat 'Assemble' (la piece montee est deja dans la voiture).
        static bool IsSwapPoint(PlayMakerFSM f)
        {
            return f.FsmVariables.FindFsmGameObject("Parent") == null && f.Fsm.GetState("Assemble") != null && f.Fsm.GetState("Assemble 2") == null;
        }

        // Etat ou mene le clic ('Wait for assembly' -ASSEMBLE->) : 'Wait' le plus souvent.
        static string ClickTarget(PlayMakerFSM f)
        {
            FsmState w = f.Fsm.GetState("Wait for assembly");
            if (w != null) foreach (FsmTransition t in w.Transitions) if (t.EventName == "ASSEMBLE") return t.ToState;
            return null;
        }

        static int MscConsider(PlayMakerFSM f)
        {
            if (waitingSet.Contains(f)) return 0;
            bool known;
            if (f.FsmName == "Screw") known = screws.Contains(f) || !IsMscBolt(f);
            else if (f.FsmName == "Removal") known = mscRemovals.Contains(f) || f.Fsm.GetState("Remove part") == null;
            else known = mscPointOf.ContainsKey(f) || !IsMscPoint(f);
            if (known) return 0;
            Replay.Claim(f, "pieces");
            if (MscTryHook(f)) return 1;
            waitingSet.Add(f);
            waiting.Add(f);
            return 0;
        }

        static bool MscTryHook(PlayMakerFSM f)
        {
            if (f.FsmName == "Screw")
            {
                FsmState s3 = f.Fsm.GetState("Wait 3"), s4 = f.Fsm.GetState("Wait 4");
                if (!s3.IsInitialized || !s4.IsInitialized) return false;
                var b = new Bolt { F = f, Key = BoltKey(f, f.FsmVariables.GetFsmGameObject("PartAssembled").Value) };
                if (!Inject(s3, new MscBoltHook { B = b }) || !Inject(s4, new MscBoltHook { B = b })) return false;
                screws.Add(f); bolts.Add(b);
                return true;
            }
            if (f.FsmName == "Removal")
            {
                FsmState rs = f.Fsm.GetState("Remove part");
                if (!rs.IsInitialized || !Inject(rs, new MscRemovalHook { F = f })) return false;
                mscRemovals.Add(f);
                if (f.GetComponent<PartWatch>() == null) f.gameObject.AddComponent<PartWatch>();
                return true;
            }
            bool swap = IsSwapPoint(f);
            // (echange : envoye a l'entree de 'Assemble' -- 'Check bolts' peut encore repartir en 'Drop part' ; rejoue en 'Assemble')
            string click = swap ? "Assemble" : ClickTarget(f);
            FsmState cs = click != null ? f.Fsm.GetState(click) : null;
            if (cs == null || !cs.IsInitialized) return false;
            var p = new MscPoint { Key = MscPointKey(f.transform), Fsm = f, Click = click, Swap = swap };
            if (!Inject(cs, new MscPointHook { P = p })) return false;
            mscPoints[p.Key] = p; mscPointOf[f] = p;
            return true;
        }

        static void MscScan()
        {
            int before = mscPoints.Count, beforeBolts = screws.Count, beforeRem = mscRemovals.Count;
            foreach (Object o in Game.AllFsms(0.5f))
            {
                var f = (PlayMakerFSM)o;
                if (f == null || f.hideFlags != HideFlags.None) continue;
                string n = f.FsmName;
                if (n == "Assembly" || n == "Removal" || n == "Screw") MscConsider(f);
            }
            if (mscPoints.Count != before || screws.Count != beforeBolts || mscRemovals.Count != beforeRem)
                Log.Info("pieces (MSC) : " + mscPoints.Count + " points de montage, " + mscRemovals.Count + " pieces demontables, " + screws.Count + " vis ("
                         + waiting.Count + " automates en attente de leur activation)");
        }

        static void MscCheckWaiting()
        {
            int n = 0;
            for (int i = waiting.Count - 1; i >= 0; i--)
            {
                PlayMakerFSM f = waiting[i];
                if (f == null) { waiting.RemoveAt(i); continue; }
                if (!f.gameObject.activeInHierarchy) continue;
                if (!MscTryHook(f)) continue;
                waiting.RemoveAt(i); waitingSet.Remove(f);
                n++;
            }
            if (n > 0) Log.Info("pieces (MSC) : " + n + " vis, pieces ou points accroches a leur activation");
        }

        // ---------------------------------------------------------------- montage, demontage
        static void MscLocalInstall(MscPoint p)
        {
            GameObject part = p.Fsm.FsmVariables.GetFsmGameObject("Part").Value;
            if (!Session.Active || Time.realtimeSinceStartup - loadedAt < 20f || part == null) return;
            string id = MscPartKey(part);
            Touch(H(p.Key));
            Log.Info("montage : " + id + " sur " + p.Key);
            VehicleSync.CalmAfterPart(p.Fsm.transform);
            Session.SendAll(new NetWriter(Msg.Part).U8(Session.LocalId).Bool(true).Str(p.Key).Str(id).U8(0), true);
            mounting[part] = Time.realtimeSinceStartup + 1f; mountingAt[part] = p.Fsm.transform;
        }

        // Attache (FixedJoint) d'une piece disparue ici alors que la voiture est menee par un autre : 'Mouse off' (HasComponent)
        // envoyait la piece en 'Remove part' -- detachee, Installed=false, par terre ici seulement (les 4 ailes et pare-chocs de la
        // SATSUMA chez l'invite qui venait d'en descendre, 8 pieces chez l'hote pendant 1,5 s de copie ; 20/10). Une vraie casse
        // chez celui qui la mene arrive par Part (rejouee avec applying) : ici la piece est remise a sa pose de montage (locale
        // nulle sous son pivot, comme 'Assemble 2'), une attache neuve posee, et l'automate renvoye en 'Mouse off' AVANT les
        // actions de 'Remove part' (SwitchLater). 'BoltCheck' recoit la nouvelle attache (sa variable Joint, lue a l'entree de
        // Bolts ON 2 seulement).
        static int keepLogs;
        static readonly Dictionary<Joint, Rigidbody> keptJoints = new Dictionary<Joint, Rigidbody>();
        static float nextKept;
        static void MscCheckKept(float now)
        {
            if (keptJoints.Count == 0 || now < nextKept) return;
            nextKept = now + 0.5f;
            List<Joint> done = null;
            foreach (KeyValuePair<Joint, Rigidbody> kv in keptJoints)
            {
                if (kv.Key != null && kv.Value != null && VehicleSync.Authority(kv.Value) != Session.LocalId) continue;
                if (kv.Key != null && float.IsInfinity(kv.Key.breakForce)) { kv.Key.breakForce = 200f; kv.Key.breakTorque = 200f; }
                if (done == null) done = new List<Joint>();
                done.Add(kv.Key);
            }
            if (done != null) foreach (Joint j in done) keptJoints.Remove(j);
        }
        static bool MscKeepOnCopy(PlayMakerFSM rem)
        {
            if (!Session.Active) return false;
            Transform car = VehicleSync.CarRoot(rem.transform);
            Rigidbody cb = car != null ? car.GetComponent<Rigidbody>() : null;
            if (cb == null || VehicleSync.Authority(cb) == Session.LocalId) return false;
            if (rem.GetComponent<FixedJoint>() != null || rem.transform.parent == null) return false;   // (decrochage {Detach} du jeu, pas une attache disparue)
            Rigidbody to = rem.transform.parent.GetComponentInParent<Rigidbody>();
            if (to == null) to = cb;
            string id = MscPartKey(rem.gameObject);
            Rigidbody prb = rem.GetComponent<Rigidbody>();
            rem.transform.localPosition = Vector3.zero; rem.transform.localRotation = Quaternion.identity;
            if (prb != null) { prb.velocity = Vector3.zero; prb.angularVelocity = Vector3.zero; if (!prb.isKinematic) { prb.position = rem.transform.position; prb.rotation = rem.transform.rotation; } }
            FixedJoint fj = rem.gameObject.AddComponent<FixedJoint>();
            fj.connectedBody = to;
            VehicleSync.AdoptJoint(cb, fj, new Vector2(200f, 200f));   // (InitialAttachForce du montage)
            // Voiture garee menee par l'autre, libre ici (pas une copie cinematique) : l'attache neuve a 200 recassait a chaque pas
            // tant que la piece restait pressee (pare-chocs contre la porte du garage apres une poussee : 60 casses en 1 s, force
            // 1,1e9, la caisse secouee ici -- partie de JD et Kristof, 24/10, rejoue par Autotest=mscmontage PousseA). Incassable
            // tant qu'un autre la mene ; ses limites quand elle redevient la notre (MscTick).
            if (!float.IsInfinity(fj.breakForce)) { fj.breakForce = Mathf.Infinity; fj.breakTorque = Mathf.Infinity; keptJoints[fj] = cb; }
            PlayMakerFSM bc = Game.FsmOn(rem.gameObject, "BoltCheck");
            FsmObject jv = bc != null ? bc.FsmVariables.FindFsmObject("Joint") : null;
            if (jv != null) jv.Value = fj;
            bool back = Game.SwitchLater(rem, "Mouse off");
            if (keepLogs++ < 40 || keepLogs % 50 == 0)
                Log.Warn("piece " + id + " : attache disparue ici sur la copie (voiture menee par #" + VehicleSync.Authority(cb) + "), remise sur " + to.name
                         + (prb != null ? ", corps " + (prb.isKinematic ? "cinematique" : "libre") : "") + (back ? "" : " (automate non renvoye en Mouse off)"));
            return back;
        }

        static void MscLocalRemove(PlayMakerFSM rem, bool breakoff)
        {
            if (!Session.Active || Time.realtimeSinceStartup - loadedAt < 20f) return;
            string id = MscPartKey(rem.gameObject);
            FsmGameObject trig = rem.FsmVariables.FindFsmGameObject("Trigger");
            string key = trig != null && trig.Value != null ? MscPointKey(trig.Value.transform) : "";
            if (key.Length > 0) Touch(H(key));
            if (breakoff)
            {
                // Casse : envoyee seulement par celui qui fait autorite sur la voiture (sa copie ici a pu s'emballer) -- comme MWC.
                Transform car = VehicleSync.CarRoot(rem.transform);
                Rigidbody cb = car != null ? car.GetComponent<Rigidbody>() : null;
                if (cb != null && VehicleSync.Authority(cb) != Session.LocalId)
                {
                    Rigidbody prb = rem.GetComponent<Rigidbody>();
                    Log.Warn("piece " + id + " cassee ici sous l'autorite d'un autre : pas envoyee (joints " + rem.GetComponents<Joint>().Length + ", parent "
                             + (rem.transform.parent != null ? rem.transform.parent.name : "aucun") + (prb != null ? ", corps " + (prb.isKinematic ? "cinematique" : "libre") + " v " + prb.velocity.magnitude.ToString("F1") : "") + ")");
                    return;
                }
                if (TooMany(car != null ? car.name : key, Time.realtimeSinceStartup, BurstCalm, WindowCalm)) { Log.Warn("piece " + id + " cassee : trop de casses, pas envoyee"); return; }
            }
            // (echange : la nouvelle piece libre n'existe qu'apres les actions de 'Remove part' -- envoye a l'image suivante, MscTick)
            if (rem.FsmVariables.FindFsmGameObject("SpawnPart") != null) { mscOut.Add(new MscOut { Rem = rem, Key = key, Id = id, Flags = breakoff ? 1 : 0 }); return; }
            Log.Info((breakoff ? "casse : " : "demontage : ") + id + " de " + key);
            Session.SendAll(new NetWriter(Msg.Part).U8(Session.LocalId).Bool(false).Str(key).Str(id).U8(breakoff ? 1 : 0), true);
            Props.SettleHere(rem.gameObject);
        }

        // Temoin sur chaque piece de MSC (24/10, partie de JD et Kristof : aile gauche cassee 0,05 a 0,3 s apres chaque montage chez
        // l'hote, tableau de bord casse a la pose des compteurs) : a la rupture de son attache, la force et les derniers contacts
        // (autre collisionneur, vitesse relative, il y a combien) -- ce qui l'a poussee.
        public class PartWatch : MonoBehaviour
        {
            readonly string[] what = new string[6]; readonly float[] at = new float[6]; int next;
            void OnCollisionEnter(Collision c)
            {
                if (c == null || c.collider == null) return;
                Rigidbody o = c.rigidbody;
                what[next] = Recon.Path(c.collider.transform) + (o == null ? " (fixe)" : o.isKinematic ? " (cinematique)" : "") + " " + (c.relativeVelocity.magnitude * 3.6f).ToString("F0") + " km/h";
                at[next] = Time.realtimeSinceStartup; next = (next + 1) % what.Length;
            }
            void OnJointBreak(float force) { try { OnPartJointBreak(this, force); } catch (System.Exception e) { Replay.HookError(e); } }
            public string Recent(float within)
            {
                var sb = new System.Text.StringBuilder();
                float now = Time.realtimeSinceStartup;
                for (int i = 0; i < what.Length; i++)
                    if (what[i] != null && now - at[i] < within) sb.Append(sb.Length > 0 ? " ; " : "").Append(what[i]).Append(" il y a ").Append((now - at[i]).ToString("F2")).Append(" s");
                return sb.Length > 0 ? sb.ToString() : "aucun";
            }
        }
        static int breakLogs;
        static void OnPartJointBreak(PartWatch w, float force)
        {
            if (!Session.Active || breakLogs++ >= 60) return;
            Joint j = w.GetComponent<Joint>();
            Rigidbody prb = w.GetComponent<Rigidbody>();
            Transform car = VehicleSync.CarRoot(w.transform);
            Rigidbody cb = car != null ? car.GetComponent<Rigidbody>() : null;
            Log.Info("piece " + MscPartKey(w.gameObject) + " : attache rompue, force " + force.ToString("F0") + (j != null ? " (limite " + j.breakForce.ToString("F0") + ")" : "")
                     + (prb != null ? ", piece v " + prb.velocity.magnitude.ToString("F2") + (prb.isKinematic ? " cin" : "") : "")
                     + (cb != null ? ", voiture v " + cb.velocity.magnitude.ToString("F2") + " rot " + cb.angularVelocity.magnitude.ToString("F2") + (cb.isKinematic ? " cin" : "") + " menee par #" + VehicleSync.Authority(cb) : "")
                     + " ; contacts 1 s : " + w.Recent(1f));
        }

        // Essais ([Test] Autotest=mscmontage) : clic de montage comme celui du joueur -- la piece tenue (cinematique) au declencheur,
        // 'Wait for assembly', puis ASSEMBLE a l'image suivante.
        static PlayMakerFSM testClick; static int testClickFrame;
        public static string MscTestClick(string pointKey, string partKey)
        {
            MscPoint p;
            if (!mscPoints.TryGetValue(pointKey, out p) || p.Fsm == null) { MscScan(); mscPoints.TryGetValue(pointKey, out p); }
            if (p == null || p.Fsm == null) return "point " + pointKey + " introuvable";
            GameObject part = MscFindPart(partKey) ?? Props.ObjectOf("w:" + partKey);
            if (part == null) return "piece " + partKey + " introuvable";
            if (!p.Fsm.gameObject.activeInHierarchy) return "point " + pointKey + " eteint";
            Rigidbody prb = part.GetComponent<Rigidbody>();
            if (prb != null) { prb.velocity = Vector3.zero; prb.angularVelocity = Vector3.zero; prb.isKinematic = true; }
            FsmGameObject pv = p.Fsm.FsmVariables.FindFsmGameObject("Parent");
            part.transform.position = p.Fsm.transform.position;
            part.transform.rotation = pv != null && pv.Value != null ? pv.Value.transform.rotation : p.Fsm.transform.rotation;
            p.Fsm.FsmVariables.GetFsmGameObject("Part").Value = part;
            Game.SetState(p.Fsm, "Wait for assembly");
            testClick = p.Fsm; testClickFrame = Time.frameCount + 2;
            return "clic de montage de " + partKey + " sur " + pointKey;
        }
        public static string MscTestHolds(string pointKey)
        {
            MscPoint p;
            if (!mscPoints.TryGetValue(pointKey, out p) || p.Fsm == null) return "point introuvable";
            string id = MscInstalledId(p);
            return id.Length > 0 ? "monte : " + id : "vide (etat " + p.Fsm.ActiveStateName + ")";
        }
        static void MscTestClickTick()
        {
            if (testClick == null || Time.frameCount < testClickFrame) return;
            PlayMakerFSM f = testClick; testClick = null;
            Log.Info("autotest : ASSEMBLE dans " + f.ActiveStateName);
            f.SendEvent("ASSEMBLE");
        }

        class MscOut { public PlayMakerFSM Rem; public string Key, Id; public int Flags; }
        static readonly List<MscOut> mscOut = new List<MscOut>();
        static int mscOutSerial;

        // Demontage d'une piece « echange » fait ici : la nouvelle piece libre ({Part} de 'Removal') recoit une cle commune.
        static void MscTick()
        {
            if (testClick != null) MscTestClickTick();
            MscCheckKept(Time.realtimeSinceStartup);
            if (mscOut.Count == 0) return;
            foreach (MscOut o in mscOut)
            {
                if (o.Rem == null) continue;
                FsmGameObject pv = o.Rem.FsmVariables.FindFsmGameObject("Part");
                GameObject loose = pv != null ? pv.Value : null;
                string outKey = "";
                if (loose != null && loose != o.Rem.gameObject)
                {
                    outKey = "w:msc-out/" + o.Id + "#" + Session.LocalId + "-" + (++mscOutSerial);
                    Props.Adopt(loose, outKey);
                    Props.SettleHere(loose);
                }
                Log.Info(((o.Flags & 1) != 0 ? "casse : " : "demontage : ") + o.Id + " de " + o.Key + (outKey.Length > 0 ? ", piece libre " + outKey : ", pas de piece libre"));
                Session.SendAll(new NetWriter(Msg.Part).U8(Session.LocalId).Bool(false).Str(o.Key).Str(o.Id).U8(o.Flags).Str(outKey), true);
            }
            mscOut.Clear();
        }

        // Montage « echange » de l'autre : 'Assemble' allume la piece montee d'ici et detruit la copie de la piece libre (si elle est ici).
        static void MscSwapInstall(MscPoint p, GameObject part, string id, string key, int who)
        {
            if (!p.Fsm.gameObject.activeInHierarchy)
            {
                Log.Info("piece " + id + " deja montee sur " + key);
                if (part != null && Props.Vanish(id)) Log.Info("piece " + id + " : copie libre d'ici cachee");
                return;
            }
            if (part != null) Props.ReleaseRemote(part);
            p.Fsm.FsmVariables.GetFsmGameObject("Part").Value = part;
            VehicleSync.CalmAfterPart(p.Fsm.transform);
            Trace.Watch(p.Fsm, "point " + key, 8f);
            Game.SetState(p.Fsm, p.Click);
            if (part != null) Props.Vanish(id);   // (detruite par 'Assemble' : plus suivie)
            Log.Info("piece " + id + " montee (echange) sur " + key + " (joueur #" + who + (part == null ? ", pas de copie libre ici" : "") + ")");
        }

        static bool MscHolds(PlayMakerFSM point, GameObject part)
        {
            if (point.gameObject.activeSelf) return false;   // (declencheur eteint par 'End' : piece montee)
            FsmGameObject p = point.FsmVariables.FindFsmGameObject("Part");
            return p != null && p.Value == part;
        }

        static void MscOnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            bool install = r.Bool();
            string key = r.Str(), id = r.Str();
            int flags = r.More ? r.U8() : 0;
            string outKey = r.More ? r.Str() : "";
            Touch(H(key));
            if (Session.IsHost)
            {
                var fw = new NetWriter(Msg.Part).U8(who).Bool(install).Str(key).Str(id).U8(flags);
                if (outKey.Length > 0) fw.Str(outKey);
                Session.Broadcast(fw, true, who);
            }
            // (piece libre « echange » : pas d'automate, retrouvee par Props sous la meme cle)
            GameObject part = MscFindPart(id) ?? Props.ObjectOf(id);
            applying = true; Replay.Depth++;
            try
            {
                if (install)
                {
                    MscPoint p;
                    if (!mscPoints.TryGetValue(key, out p) || p.Fsm == null) { MscScan(); mscPoints.TryGetValue(key, out p); }
                    if (p == null || p.Fsm == null) { Log.Warn("point " + key + " introuvable ici"); return; }
                    if (p.Swap) { MscSwapInstall(p, part, id, key, who); return; }
                    if (part == null) { Log.Warn("piece " + id + " introuvable ici"); return; }
                    if (MscHolds(p.Fsm, part)) { Log.Info("piece " + id + " deja montee"); return; }
                    if (!p.Fsm.gameObject.activeInHierarchy) { Log.Warn("point " + key + " eteint ici (deja occupe ?) : " + id + " pas montee"); return; }
                    p.Fsm.FsmVariables.GetFsmGameObject("Part").Value = part;
                    Props.ReleaseRemote(part);
                    // A sa place sur son pivot ({Parent} : SetParent de 'Assemble 2') AVANT le joint de 'Set joint 2' : posee au
                    // declencheur, le joint etait tendu au SetParent et cassait aussitot (force 1100 : portiere tombee chez l'invite).
                    FsmGameObject pv = p.Fsm.FsmVariables.FindFsmGameObject("Parent");
                    Transform at = pv != null && pv.Value != null ? pv.Value.transform : p.Fsm.transform;
                    part.transform.position = at.position; part.transform.rotation = at.rotation;
                    // Et cinematique, comme en main : 'Assemble 2' la rend libre (SetIsKinematic false). Libre pendant 'Wait 2', dans la
                    // caisse figee ('Freeze car'), elle etait repoussee et son joint cassait (0 joint, portiere retombee chez l'invite).
                    Rigidbody prb = part.GetComponent<Rigidbody>();
                    if (prb != null) { if (!prb.isKinematic) { prb.velocity = Vector3.zero; prb.angularVelocity = Vector3.zero; } prb.isKinematic = true; }
                    VehicleSync.CalmAfterPart(p.Fsm.transform);
                    mounting[part] = Time.realtimeSinceStartup + 1f; mountingAt[part] = p.Fsm.transform;
                    Trace.Watch(p.Fsm, "point " + key, 8f);
                    PlayMakerFSM prem = Game.FsmOn(part, "Removal");
                    if (prem != null) Trace.Watch(prem, "piece " + id, 8f);
                    Game.SetState(p.Fsm, p.Click);
                    Freshen(part, p.Fsm, id);
                    Log.Info("piece " + id + " montee sur " + key + " (joueur #" + who + ")");
                }
                else
                {
                    if (part == null) { Log.Warn("piece " + id + " introuvable ici"); return; }
                    PlayMakerFSM rem = Game.FsmOn(part, "Removal");
                    if (rem == null) { Log.Warn("piece " + id + " sans Removal ici"); return; }
                    FsmGameObject trig = rem.FsmVariables.FindFsmGameObject("Trigger");
                    if (trig != null && trig.Value != null && trig.Value.activeSelf) { Log.Info("piece " + id + " deja demontee"); return; }
                    rem.enabled = true;
                    Game.SetState(rem, "Remove part");
                    // (echange : la nouvelle piece libre d'ici prend la cle de celle de l'autre et suit sa chute)
                    FsmGameObject lv = rem.FsmVariables.FindFsmGameObject("SpawnPart") != null ? rem.FsmVariables.FindFsmGameObject("Part") : null;
                    GameObject loose = lv != null && lv.Value != null && lv.Value != part ? lv.Value : null;
                    if (loose != null && outKey.Length > 0) { Props.Adopt(loose, outKey); Props.HoldFromRemote(loose, who); }
                    else Props.HoldFromRemote(part, who);
                    Log.Info("piece " + id + ((flags & 1) != 0 ? " cassee de " : " demontee de ") + key + " (joueur #" + who + ")");
                }
            }
            finally { applying = false; Replay.Depth--; }
        }

        // ---------------------------------------------------------------- vis
        static void MscLocalBolt(Bolt b)
        {
            if (!Session.Active || Time.realtimeSinceStartup - loadedAt < 20f) return;
            string part = MscPartKey(b.F.FsmVariables.GetFsmGameObject("PartAssembled").Value);
            if (part.Length == 0) return;
            Touch(H(part + "|" + b.Key));
            int stage = b.F.FsmVariables.GetFsmInt("Stage").Value;
            Session.SendAll(new NetWriter(Msg.Bolt).U8(Session.LocalId).Str(part).Str(b.Key).U8(Mathf.Clamp(stage, 0, 255)), true);
        }

        static Bolt MscFindBolt(string part, string key)
        {
            foreach (Bolt b in bolts)
                if (b.F != null && b.Key == key && MscPartKey(b.F.FsmVariables.GetFsmGameObject("PartAssembled").Value) == part) return b;
            return null;
        }


        // ---------------------------------------------------------------- controle periodique, My Summer Car (23/10)
        // Meme principe que sur MWC (Parts.Digest) : points 'Assembly' (hors points « echange », sans piece libre a renvoyer) et vis
        // 'Screw' (Stage). Piece en trop ici -> 'Remove part' de son 'Removal' ; piece manquante -> demandee, l'hote renvoie le
        // montage (Msg.Part comme MscLocalInstall) ; vis au Stage de l'hote.
        static Dictionary<uint, MscPoint> mscPointsByHash; static int mscPointsByHashN = -1;
        static string MscBoltKeyOf(Bolt b) { return MscPartKey(b.F.FsmVariables.GetFsmGameObject("PartAssembled").Value) + "|" + b.Key; }
        static void MscHashMaps()
        {
            if (mscPointsByHash == null || mscPointsByHashN != mscPoints.Count)
            {
                mscPointsByHash = new Dictionary<uint, MscPoint>();
                foreach (KeyValuePair<string, MscPoint> kv in mscPoints) mscPointsByHash[H(kv.Key)] = kv.Value;
                mscPointsByHashN = mscPoints.Count;
            }
            if (boltsByHash == null || boltsByHashN != bolts.Count)
            {
                boltsByHash = new Dictionary<uint, Bolt>();
                foreach (Bolt b in bolts) if (b.F != null) boltsByHash[H(MscBoltKeyOf(b))] = b;
                boltsByHashN = bolts.Count;
            }
        }
        static string MscInstalledId(MscPoint p)
        {
            FsmGameObject pv = p.Fsm.FsmVariables.FindFsmGameObject("Part");
            GameObject part = pv != null ? pv.Value : null;
            return part != null && MscHolds(p.Fsm, part) ? MscPartKey(part) : "";
        }
        static int lastDigestMounted = -1;
        static void MscDigest(float now)
        {
            NetWriter w = null; int n = 0, total = 0, totalB = 0, mounted = 0;
            foreach (KeyValuePair<string, MscPoint> kv in mscPoints)
            {
                MscPoint p = kv.Value;
                if (p.Fsm == null || p.Swap) continue;
                string id = MscInstalledId(p);
                if (id.Length > 0) mounted++;
                if (w == null) { w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@pieces").U8(0); n = 0; }
                uint hk = H(p.Key);
                w.I32((int)hk).Str(id).F32(Age(hk, now)); n++; total++;
                // (coupe aussi a la taille : 40 points aux cles longues d'une SATSUMA montee depassaient Transport.MaxPayload,
                // l'envoi levait et tout le controle de ce tour etait perdu, vis comprises -- 24/10)
                if (n >= 40 || w.Length > 900) { Session.SendAll(w, true); w = null; }
            }
            if (w != null) Session.SendAll(w, true);
            w = null; n = 0;
            foreach (Bolt b in bolts)
            {
                if (b.F == null || !b.F.gameObject.activeInHierarchy) continue;
                FsmInt st = b.F.FsmVariables.FindFsmInt("Stage"); if (st == null) continue;
                if (w == null) { w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@pieces").U8(1); n = 0; }
                uint hb = H(MscBoltKeyOf(b));
                w.I32((int)hb).U8(Mathf.Clamp(st.Value, 0, 255)).F32(Age(hb, now)); n++; totalB++;
                if (n >= 100) { Session.SendAll(w, true); w = null; }
            }
            if (w != null) Session.SendAll(w, true);
            // (nombre de points montes : journal aussi quand il change -- une chute sans geste de joueur = etat de l'hote faux)
            if (digestLogs++ < 3 || now >= lastDigestLog + 120f || mounted != lastDigestMounted) { lastDigestLog = now; Log.Info("pieces (MSC) : controle envoye aux invites (" + total + " points dont " + mounted + " montes, " + totalB + " vis)"); }
            lastDigestMounted = mounted;
        }
        static float lastWouldLog = -100f;
        static void MscOnDigest(int who, NetReader r, int kind, float now)
        {
            MscHashMaps();
            List<int> ask = null; int removed = 0, fixedBolts = 0, unknown = 0, resent = 0;
            if (kind == 0)
            {
                var rows = new List<KeyValuePair<uint, KeyValuePair<string, float>>>();
                while (r.More) { uint h0 = (uint)r.I32(); string id0 = r.Str(); float a0 = r.F32(); rows.Add(new KeyValuePair<uint, KeyValuePair<string, float>>(h0, new KeyValuePair<string, float>(id0, a0))); }
                int would = 0;
                System.Text.StringBuilder wl = null;
                foreach (var row in rows)
                {
                    MscPoint q;
                    if (!mscPointsByHash.TryGetValue(row.Key, out q) || q.Fsm == null || q.Swap || Recent(row.Key, now)) continue;
                    FsmGameObject qv = q.Fsm.FsmVariables.FindFsmGameObject("Part");
                    GameObject qp = qv != null ? qv.Value : null;
                    string qm = MscInstalledId(q);
                    if (qm.Length == 0 || qm == row.Value.Key || mounting.ContainsKey(qp) || PendingReinstall(qp)) continue;
                    if (!(Age(row.Key, now) + 0.5f < row.Value.Value && row.Value.Value > 0f))
                    {
                        would++;
                        if (wl == null) wl = new System.Text.StringBuilder();
                        if (would <= 20) wl.Append(' ').Append(q.Key).Append(" [ici ").Append(qm).Append(", hote \"").Append(row.Value.Key).Append("\"]");
                    }
                }
                // (quels points : cause du controle de l'hote qui demonte une SATSUMA montee, Markos 24/10 -- une fois par minute)
                if (would > 0 && now >= lastWouldLog + 60f) { lastWouldLog = now; Log.Info("pieces (MSC) : controle de l'hote : " + would + " point(s) monte(s) ici, pas chez l'hote :" + wl); }
                bool trusted = DigestTrusted(would, now);
                foreach (var row in rows)
                {
                    uint h = row.Key; string id = row.Value.Key; float hostAge = row.Value.Value;
                    MscPoint p;
                    if (!mscPointsByHash.TryGetValue(h, out p) || p.Fsm == null) { unknown++; continue; }
                    if (Recent(h, now) || p.Swap) continue;
                    FsmGameObject pv = p.Fsm.FsmVariables.FindFsmGameObject("Part");
                    GameObject part = pv != null ? pv.Value : null;
                    string mine = MscInstalledId(p);
                    if (mine == id) continue;
                    if (mine.Length > 0 && (mounting.ContainsKey(part) || PendingReinstall(part))) continue;
                    if (Age(h, now) + 0.5f < hostAge && hostAge > 0f)
                    {
                        // Mon geste est le plus recent : l'hote se met a jour (montage comme MscLocalInstall, demontage comme MscLocalRemove)
                        if (mine.Length > 0) Session.SendAll(new NetWriter(Msg.Part).U8(Session.LocalId).Bool(true).Str(p.Key).Str(mine).U8(0), true);
                        else if (id.Length > 0) Session.SendAll(new NetWriter(Msg.Part).U8(Session.LocalId).Bool(false).Str(p.Key).Str(id).U8(0), true);
                        Log.Info("piece " + (mine.Length > 0 ? mine + " montee" : id + " demontee") + " ici sur " + p.Key + ", plus recent que chez l'hote : renvoye");
                        Touch(h); resent++;
                        continue;
                    }
                    if (mine.Length > 0 && !trusted) continue;   // (controle pas fiable : rien de demonte, rien demande)
                    if (mine.Length > 0)
                    {
                        PlayMakerFSM rem = Game.FsmOn(part, "Removal");
                        if (rem != null)
                        {
                            VehicleSync.CalmAfterPart(rem.transform);
                            applying = true; Replay.Depth++;
                            try { rem.enabled = true; Game.SetState(rem, "Remove part"); }
                            finally { applying = false; Replay.Depth--; }
                            Props.HoldFromRemote(part, who);
                            Log.Info("piece " + mine + " demontee de " + p.Key + " au controle de l'hote : pas montee chez lui" + (id.Length > 0 ? " (il y a " + id + ")" : ""));
                            removed++;
                        }
                    }
                    if (id.Length > 0 && !AskedLately(h, now)) { if (ask == null) ask = new List<int>(); if (ask.Count < 20) ask.Add((int)h); }
                    Touch(h);
                }
                if (ask != null)
                {
                    var w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@piecesdemande");
                    foreach (int h in ask) w.I32(h);
                    Session.SendToHost(w, true);
                }
            }
            else
            {
                while (r.More)
                {
                    uint h = (uint)r.I32(); int target = r.U8(); float hostAge = r.F32();
                    Bolt b;
                    if (!boltsByHash.TryGetValue(h, out b) || b.F == null) { unknown++; continue; }
                    if (Recent(h, now) || !b.F.gameObject.activeInHierarchy) continue;
                    FsmInt st = b.F.FsmVariables.FindFsmInt("Stage");
                    if (st == null || st.Value == target) continue;
                    string part = MscPartKey(b.F.FsmVariables.GetFsmGameObject("PartAssembled").Value);
                    if (Age(h, now) + 0.5f < hostAge && hostAge > 0f)
                    {
                        Session.SendAll(new NetWriter(Msg.Bolt).U8(Session.LocalId).Str(part).Str(b.Key).U8(Mathf.Clamp(st.Value, 0, 255)), true);
                        Log.Info("vis " + part + "/" + b.Key + " = " + st.Value + " ici, plus recent que chez l'hote (" + target + ") : renvoyee");
                        Touch(h); resent++;
                        continue;
                    }
                    if (MscApplyBolt(who, part, b.Key, target, true)) { fixedBolts++; Touch(h); }
                }
            }
            int asked = ask != null ? ask.Count : 0;
            if (removed + fixedBolts + asked + resent > 0)
                Log.Info("pieces (MSC) : controle de l'hote : " + removed + " demontee(s) ici, " + asked + " demandee(s), " + fixedBolts + " vis reglee(s), " + resent + " geste(s) d'ici plus recent(s) renvoye(s)" + (unknown > 0 ? ", " + unknown + " inconnue(s) ici" : ""));
        }
        static void MscOnDigestAsk(int who, NetReader r)
        {
            MscHashMaps();
            int n = 0;
            while (r.More && n < 20)
            {
                uint h = (uint)r.I32(); n++;
                MscPoint p;
                if (!mscPointsByHash.TryGetValue(h, out p) || p.Fsm == null || p.Swap) continue;
                string id = MscInstalledId(p);
                if (id.Length == 0) continue;
                Session.Broadcast(new NetWriter(Msg.Part).U8(Session.LocalId).Bool(true).Str(p.Key).Str(id).U8(0), true);
                Log.Info("pieces (MSC) : montage de " + id + " sur " + p.Key + " renvoye a la demande de #" + who);
            }
        }

        static bool MscApplyBolt(int who, string part, string key, int target, bool last)
        {
            Bolt b = MscFindBolt(part, key);
            if (b == null || !b.F.gameObject.activeInHierarchy)
            {
                if (last) Log.Warn("vis " + part + "/" + key + (b == null ? " introuvable ici (piece pas montee ici ?)" : " inactive ici"));
                return false;
            }
            FsmInt st = b.F.FsmVariables.GetFsmInt("Stage");
            applying = true; Replay.Depth++;
            try
            {
                for (int guard = 0; st.Value != target && guard < 10; guard++)
                    b.F.SendEvent(st.Value < target ? "TIGHTEN" : "UNTIGHTEN");
            }
            finally { applying = false; Replay.Depth--; }
            if (st.Value != target)
            {
                if (last) Log.Warn("vis " + part + "/" + key + " : " + st.Value + " au lieu de " + target);
                return false;
            }
            Log.Info("vis " + part + "/" + key + " = " + target + " (joueur #" + who + ")");
            return true;
        }
    }
}
