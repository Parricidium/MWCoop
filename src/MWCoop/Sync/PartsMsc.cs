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
    public static partial class Parts
    {
        class MscPoint { public string Key; public PlayMakerFSM Fsm; public string Click; }
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
                    if (!applying && Replay.Depth == 0 && prev != null && prev.Name == "Wait for assembly") MscLocalInstall(P);
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
                    if (!applying && Replay.Depth == 0) MscLocalRemove(F, prev != null && prev.Name == "Mouse off");
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
            mscPoints.Clear(); mscPointOf.Clear(); mscRemovals.Clear(); mscFirstKey.Clear(); mscParts.Clear(); mscPartsAt = -100f;
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
                return true;
            }
            string click = ClickTarget(f);
            FsmState cs = click != null ? f.Fsm.GetState(click) : null;
            if (cs == null || !cs.IsInitialized) return false;
            var p = new MscPoint { Key = MscPointKey(f.transform), Fsm = f, Click = click };
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
            Log.Info("montage : " + id + " sur " + p.Key);
            VehicleSync.CalmAfterPart(p.Fsm.transform);
            Session.SendAll(new NetWriter(Msg.Part).U8(Session.LocalId).Bool(true).Str(p.Key).Str(id).U8(0), true);
            mounting[part] = Time.realtimeSinceStartup + 1f; mountingAt[part] = p.Fsm.transform;
        }

        static void MscLocalRemove(PlayMakerFSM rem, bool breakoff)
        {
            if (!Session.Active || Time.realtimeSinceStartup - loadedAt < 20f) return;
            string id = MscPartKey(rem.gameObject);
            FsmGameObject trig = rem.FsmVariables.FindFsmGameObject("Trigger");
            string key = trig != null && trig.Value != null ? MscPointKey(trig.Value.transform) : "";
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
            Log.Info((breakoff ? "casse : " : "demontage : ") + id + " de " + key);
            Session.SendAll(new NetWriter(Msg.Part).U8(Session.LocalId).Bool(false).Str(key).Str(id).U8(breakoff ? 1 : 0), true);
            Props.SettleHere(rem.gameObject);
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
            if (Session.IsHost) Session.Broadcast(new NetWriter(Msg.Part).U8(who).Bool(install).Str(key).Str(id).U8(flags), true, who);
            GameObject part = MscFindPart(id);
            if (part == null) { Log.Warn("piece " + id + " introuvable ici"); return; }
            applying = true; Replay.Depth++;
            try
            {
                if (install)
                {
                    MscPoint p;
                    if (!mscPoints.TryGetValue(key, out p) || p.Fsm == null) { MscScan(); mscPoints.TryGetValue(key, out p); }
                    if (p == null || p.Fsm == null) { Log.Warn("point " + key + " introuvable ici"); return; }
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
                    PlayMakerFSM rem = Game.FsmOn(part, "Removal");
                    if (rem == null) { Log.Warn("piece " + id + " sans Removal ici"); return; }
                    FsmGameObject trig = rem.FsmVariables.FindFsmGameObject("Trigger");
                    if (trig != null && trig.Value != null && trig.Value.activeSelf) { Log.Info("piece " + id + " deja demontee"); return; }
                    rem.enabled = true;
                    Game.SetState(rem, "Remove part");
                    Props.HoldFromRemote(part, who);
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
            int stage = b.F.FsmVariables.GetFsmInt("Stage").Value;
            Session.SendAll(new NetWriter(Msg.Bolt).U8(Session.LocalId).Str(part).Str(b.Key).U8(Mathf.Clamp(stage, 0, 255)), true);
        }

        static Bolt MscFindBolt(string part, string key)
        {
            foreach (Bolt b in bolts)
                if (b.F != null && b.Key == key && MscPartKey(b.F.FsmVariables.GetFsmGameObject("PartAssembled").Value) == part) return b;
            return null;
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
