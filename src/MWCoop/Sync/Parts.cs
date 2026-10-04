using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Pieces de voiture : chacune porte un automate 'Data' dont la variable ID (VIN413C1 = type
    // 413C, copie 1) est la meme chez tous les joueurs qui ont charge la meme sauvegarde : c'est
    // leur identifiant reseau. Les points de montage (VINP_*, automate 'Data' avec les etats
    // 'Install 2' et 'Remove part') sont reperes par l'ID de la piece qui les porte + chemin
    // relatif, ou par leur chemin complet s'ils sont sur la voiture.
    //  - montage : une action injectee au debut de 'Install 2' envoie (point, piece, AssemblyID) ;
    //    chez les autres : piece amenee au point, INSTALL a la piece, point mis dans 'Install 2'.
    //  - demontage : 'Remove part', rejoue tel quel (le jeu rend le Rigidbody et detache la piece).
    // Automates trouves inactifs (vis d'une piece pas encore montee, point d'une piece rangee) : gardes de
    // cote et accroches des que leur objet s'active (montage, piece sortie du carton), sans attendre le
    // releve suivant -- les premieres vis serrees juste apres un montage partaient sinon a la trappe.
    // Apres un montage (ici ou chez un autre), le sous-arbre de la piece et du point est revu plusieurs
    // fois dans les 3 s (pieces achetees depuis le dernier releve), et les commandes que la piece apporte a
    // la voiture (boite a gants, jauges, interrupteurs, cablage) sont cherchees tout de suite (Jobs.SoonScan).
    public static class Parts
    {
        class Point { public string Key; public PlayMakerFSM Fsm; }
        static readonly Dictionary<string, Point> points = new Dictionary<string, Point>();
        static readonly HashSet<PlayMakerFSM> hooked = new HashSet<PlayMakerFSM>();
        static float nextScan = -1, loadedAt;
        static bool applying;

        class Hook : ModHook
        {
            public override string Module { get { return "pieces"; } }
            public Point P;
            public bool Install;
            public override void OnEnter()
            {
                try { if (!applying && Replay.Depth == 0) OnLocal(P, Install); } catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        // Vis : automate 'Screw' sous chaque piece. Tight?/Loose? bornent (0..8), puis 'Screw' ajoute
        // ScrewInt (+1/-1) a BoltTightness et a la Tightness de la piece, et envoie BOLTING.
        // Une action injectee dans 'Screw' envoie (piece, vis, valeur visee) ; ailleurs on rejoue
        // TIGHTEN/UNTIGHTEN jusqu'a cette valeur : meme position de la vis, meme serrage de la piece.
        // Piece : son ID, ou "p:" + chemin si elle n'en a pas (cablage de la CORRIS : DatabaseWiring/...).
        // Vis : son chemin depuis la piece (complet si la piece n'est pas un de ses parents : cablage, supports
        // du moteur sur la caisse), chaque maillon avec son rang parmi ses freres du meme nom (BoltPM#3), puis
        // son premier enfant (bolt3). L'ancienne cle (premier enfant seul) confondait les deux vis de reglage
        // de la cremaillere, ou la vis de vidange et la premiere vis du carter.
        class Bolt { public PlayMakerFSM F; public string Key; }
        static readonly HashSet<PlayMakerFSM> screws = new HashSet<PlayMakerFSM>();
        static readonly List<Bolt> bolts = new List<Bolt>();

        class BoltHook : ModHook
        {
            public override string Module { get { return "pieces (boulons)"; } }
            public Bolt B;
            public override void OnEnter()
            {
                try { if (!applying && Replay.Depth == 0) OnLocalBolt(B); } catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        // Automates vus inactifs (vis, points) : accroches des que leur objet s'active (revus 4 fois/s).
        static readonly List<PlayMakerFSM> waiting = new List<PlayMakerFSM>();
        static readonly HashSet<PlayMakerFSM> waitingSet = new HashSet<PlayMakerFSM>();
        static float nextWaiting;

        // Pieces tout juste montees : leur sous-arbre et celui du point revus pendant 3 s.
        class Fresh { public Transform A, B; public string Id; public float Until, Next; }
        static readonly List<Fresh> fresh = new List<Fresh>();

        // Vis recues avant d'etre accrochees ici (piece montee a l'instant chez nous aussi) : rejouees des que
        // possible, 6 s au plus, dans l'ordre d'arrivee.
        class PendingBolt { public int Who; public string Part, Key; public int Target; public float Until; }
        static readonly List<PendingBolt> pendingBolts = new List<PendingBolt>();

        public static bool IsBolt(PlayMakerFSM f)
        {
            return f.FsmName == "Screw" && f.FsmVariables.FindFsmGameObject("ThisPart") != null
                   && f.FsmVariables.FindFsmInt("BoltTightness") != null && f.Fsm.GetState("Screw") != null;
        }

        static bool IsPoint(PlayMakerFSM f)
        {
            if (f.FsmName != "Data") return false;
            if (!f.name.StartsWith("VINP") && f.Fsm.GetState("Install 2") == null) return false;
            if (f.Fsm.GetState("Install 2") == null || f.Fsm.GetState("Remove part") == null || f.FsmVariables.FindFsmGameObject("ActivePart") == null) return false;
            return !(f.transform.root.position == Vector3.zero && f.transform.root.name.StartsWith("VIN"));   // modeles
        }

        // Cle d'une piece : son ID, sinon "p:" + chemin (pieces sans ID : cablage de la CORRIS).
        static string PartKey(GameObject part)
        {
            if (part == null) return "";
            string id = IdOf(part);
            return id.Length > 0 ? id : "p:" + Recon.Path(part.transform);
        }

        static string BoltKey(PlayMakerFSM f, GameObject part)
        {
            Transform t = f.transform;
            Transform stop = part != null && t != part.transform && t.IsChildOf(part.transform) ? part.transform : null;
            string key = t.childCount > 0 ? t.GetChild(0).name : "";
            for (; t != null && t != stop; t = t.parent) key = Link(t) + "/" + key;
            return key;
        }

        // Chemin a rangs : chaque maillon suivi de son rang parmi ses freres du meme nom quand il en a
        // (LogLongPile#2). Ce rang ne bouge pas quand le jeu ajoute d'autres enfants (piece montee : derniere).
        public static string RankPath(Transform t)
        {
            string p = Link(t);
            for (Transform c = t.parent; c != null; c = c.parent) p = Link(c) + "/" + p;
            return p;
        }

        static string Link(Transform t)
        {
            Transform p = t.parent;
            if (p == null) return t.name;
            int rank = 0, same = 0;
            for (int i = 0; i < p.childCount; i++)
            {
                Transform c = p.GetChild(i);
                if (c.name != t.name) continue;
                if (c == t) rank = same;
                same++;
            }
            return same > 1 ? t.name + "#" + rank : t.name;
        }

        // Vrai : vis accrochee ; faux : a reprendre quand son objet s'activera (automate pas encore charge).
        static bool TryHookScrew(PlayMakerFSM f)
        {
            FsmState st = f.Fsm.GetState("Screw");
            if (st == null || !st.IsInitialized) return false;
            var b = new Bolt { F = f, Key = BoltKey(f, f.FsmVariables.GetFsmGameObject("ThisPart").Value) };
            if (!Inject(st, new BoltHook { B = b })) return false;
            screws.Add(f);
            bolts.Add(b);
            return true;
        }

        static bool TryHookPoint(PlayMakerFSM f)
        {
            FsmState inst = f.Fsm.GetState("Install 2"), rem = f.Fsm.GetState("Remove part");
            if (!inst.IsInitialized || !rem.IsInitialized) return false;
            var p = new Point { Key = KeyOf(f.transform), Fsm = f };
            if (!Inject(inst, new Hook { P = p, Install = true })) return false;
            if (!Inject(rem, new Hook { P = p, Install = false })) return false;
            hooked.Add(f);
            points[p.Key] = p;
            return true;
        }

        // Vis ou point vu pour la premiere fois : a nous (WorldFsms et Jobs ne le rejouent plus), accroche ou
        // mis en attente de son activation.
        static int Consider(PlayMakerFSM f)
        {
            bool screw = f.FsmName == "Screw";
            if (screw ? screws.Contains(f) || !IsBolt(f) : hooked.Contains(f) || !IsPoint(f)) return 0;
            if (waitingSet.Contains(f)) return 0;
            Replay.Claim(f, "pieces");
            if (screw ? TryHookScrew(f) : TryHookPoint(f)) return 1;
            waitingSet.Add(f);
            waiting.Add(f);
            return 0;
        }

        static void OnLocalBolt(Bolt b)
        {
            if (!Session.Active || Time.realtimeSinceStartup - loadedAt < 20f) return;
            string part = PartKey(b.F.FsmVariables.GetFsmGameObject("ThisPart").Value);
            if (part.Length == 0) return;
            int target = b.F.FsmVariables.GetFsmInt("BoltTightness").Value + b.F.FsmVariables.GetFsmInt("ScrewInt").Value;
            Session.SendAll(new NetWriter(Msg.Bolt).U8(Session.LocalId).Str(part).Str(b.Key).U8(Mathf.Clamp(target, 0, 255)), true);
        }

        public static void OnBolt(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            string part = r.Str(), key = r.Str();
            int target = r.U8();
            if (Session.IsHost) Session.Broadcast(new NetWriter(Msg.Bolt).U8(who).Str(part).Str(key).U8(target), true, who);
            // Deja des vis en attente : celle-ci passe apres elles (meme ordre que chez l'autre).
            if (pendingBolts.Count == 0 && ApplyBolt(who, part, key, target, false)) return;
            CheckWaiting();
            if (pendingBolts.Count == 0 && ApplyBolt(who, part, key, target, false)) return;
            pendingBolts.Add(new PendingBolt { Who = who, Part = part, Key = key, Target = target, Until = Time.realtimeSinceStartup + 6f });
        }

        static Bolt FindBolt(string part, string key)
        {
            foreach (Bolt b in bolts)
            {
                if (b.F == null || b.Key != key) continue;
                if (PartKey(b.F.FsmVariables.GetFsmGameObject("ThisPart").Value) == part) return b;
            }
            return null;
        }

        static bool ApplyBolt(int who, string part, string key, int target, bool last)
        {
            Bolt b = FindBolt(part, key);
            if (b == null)
            {
                if (last) Log.Warn("vis " + part + "/" + key + " introuvable ici (piece pas montee ici ?)");
                return false;
            }
            FsmInt t = b.F.FsmVariables.GetFsmInt("BoltTightness");
            applying = true; Replay.Depth++;
            try
            {
                for (int guard = 0; t.Value != target && guard < 10; guard++)
                    b.F.SendEvent(t.Value < target ? "TIGHTEN" : "UNTIGHTEN");
            }
            finally { applying = false; Replay.Depth--; }
            if (t.Value != target) Log.Warn("vis " + part + "/" + key + " : " + t.Value + " au lieu de " + target);
            else Log.Info("vis " + part + "/" + key + " = " + target + " (joueur #" + who + ")");
            return true;
        }

        // Essais : serre (ou desserre) d'un cran la premiere vis (par cle) de la piece 'id', comme la cle.
        public static string TestBolt(string id, bool tighten)
        {
            Bolt first = null;
            foreach (Bolt b in bolts)
                if (b.F != null && PartKey(b.F.FsmVariables.GetFsmGameObject("ThisPart").Value) == id && (first == null || string.CompareOrdinal(b.Key, first.Key) < 0)) first = b;
            if (first == null) return "aucune vis active pour " + id + " (" + bolts.Count + " vis suivies, " + waiting.Count + " automates en attente)";
            first.F.SendEvent(tighten ? "TIGHTEN" : "UNTIGHTEN");
            return first.Key + " = " + first.F.FsmVariables.GetFsmInt("BoltTightness").Value;
        }

        public static string BoltState(string id)
        {
            var keys = new List<string>();
            foreach (Bolt b in bolts)
                if (b.F != null && PartKey(b.F.FsmVariables.GetFsmGameObject("ThisPart").Value) == id)
                    keys.Add(b.Key + "=" + b.F.FsmVariables.GetFsmInt("BoltTightness").Value);
            keys.Sort(string.CompareOrdinal);
            GameObject part = FindByIdCached(id);
            PlayMakerFSM d = part != null ? Game.FsmOn(part, "Data") : null;
            FsmFloat tight = d != null ? d.FsmVariables.FindFsmFloat("Tightness") : null;
            return string.Join(" ", keys.ToArray()) + " | serrage piece " + (tight != null ? tight.Value.ToString() : "?");
        }

        public static void OnLevelLoaded()
        {
            screws.Clear(); bolts.Clear();
            points.Clear();
            hooked.Clear();
            waiting.Clear(); waitingSet.Clear(); fresh.Clear(); pendingBolts.Clear();
            idMap.Clear(); idMapAt = -100;
            testStep = 0;
            loadedAt = Time.realtimeSinceStartup;
            nextScan = PlayerSync.InGame ? loadedAt + 8f : -1;
        }

        // Releve complet bientot (objets crees en nombre par un autre module).
        public static void SoonScan()
        {
            if (nextScan > 0) nextScan = Mathf.Min(nextScan, Time.realtimeSinceStartup + 1f);
        }

        public static void Update()
        {
            if (nextScan < 0) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextWaiting) { nextWaiting = now + 0.25f; CheckWaiting(); }
            if (fresh.Count > 0) CheckFresh(now);
            if (pendingBolts.Count > 0) RetryBolts(now);
            if (now < nextScan) return;
            nextScan = now + 15f;   // nouvelles pieces (achats...) : nouveau passage
            Scan();
        }

        // Automates en attente dont l'objet vient de s'activer.
        static void CheckWaiting()
        {
            int n = 0;
            for (int i = waiting.Count - 1; i >= 0; i--)
            {
                PlayMakerFSM f = waiting[i];
                if (f == null) { waiting.RemoveAt(i); waitingSet.Remove(f); continue; }
                bool screw = f.FsmName == "Screw";
                if (screw ? screws.Contains(f) : hooked.Contains(f)) { waiting.RemoveAt(i); waitingSet.Remove(f); continue; }
                if (!f.gameObject.activeInHierarchy) continue;
                if (!(screw ? TryHookScrew(f) : TryHookPoint(f))) continue;   // pas encore charge : au prochain tour
                waiting.RemoveAt(i); waitingSet.Remove(f);
                n++;
            }
            if (n > 0) Log.Info("pieces : " + n + " vis ou points accroches a leur activation (" + screws.Count + " vis, " + points.Count + " points)");
        }

        static void CheckFresh(float now)
        {
            for (int i = fresh.Count - 1; i >= 0; i--)
            {
                Fresh fr = fresh[i];
                if (now < fr.Next) continue;
                fr.Next = now + 0.5f;
                int n = ScanUnder(fr.A) + ScanUnder(fr.B);
                if (n > 0) Log.Info("pieces : " + n + " vis ou points accroches juste apres le montage de " + fr.Id);
                if (now > fr.Until) fresh.RemoveAt(i);
            }
        }

        static int ScanUnder(Transform t)
        {
            if (t == null) return 0;
            int n = 0;
            foreach (PlayMakerFSM f in t.GetComponentsInChildren<PlayMakerFSM>(true))
                if (f.FsmName == "Screw" || f.FsmName == "Data") n += Consider(f);
            return n;
        }

        static void RetryBolts(float now)
        {
            int done = 0;
            while (done < pendingBolts.Count)
            {
                PendingBolt pb = pendingBolts[done];
                bool last = now > pb.Until;
                if (!ApplyBolt(pb.Who, pb.Part, pb.Key, pb.Target, last) && !last) break;   // garde l'ordre
                done++;
            }
            if (done > 0) pendingBolts.RemoveRange(0, done);
        }

        // Montage fait (ici ou rejoue) : vis et points de la piece et du point revus, commandes de la voiture cherchees.
        static void Freshen(GameObject part, PlayMakerFSM point, string id)
        {
            float now = Time.realtimeSinceStartup;
            fresh.Add(new Fresh { A = part != null ? part.transform : null, B = point != null ? point.transform : null, Id = id, Until = now + 3f, Next = now + 0.1f });
            Jobs.SoonScan();
        }

        public static string IdOf(GameObject go)
        {
            if (go == null) return "";
            PlayMakerFSM d = Game.FsmOn(go, "Data");
            FsmString s = d != null ? d.FsmVariables.FindFsmString("ID") : null;
            return s != null ? s.Value : "";
        }

        // Cle d'un point de montage : "<ID de la piece porteuse>/<chemin relatif>" ou chemin complet.
        static string KeyOf(Transform t)
        {
            string rel = t.name;
            for (Transform p = t.parent; p != null; p = p.parent)
            {
                string id = IdOf(p.gameObject);
                if (id.Length > 0) return id + "/" + rel;
                rel = p.name + "/" + rel;
            }
            return rel;
        }

        static void Scan()
        {
            int before = points.Count, beforeBolts = screws.Count;
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var f = (PlayMakerFSM)o;
                if (f.hideFlags != HideFlags.None || (f.FsmName != "Data" && f.FsmName != "Screw")) continue;
                Consider(f);
            }
            if (points.Count != before || screws.Count != beforeBolts)
                Log.Info("pieces : " + (points.Count - before) + " points de montage et " + (screws.Count - beforeBolts) + " vis de plus (" + points.Count + " points, "
                         + screws.Count + " vis, " + waiting.Count + " automates en attente de leur activation)");
        }

        static bool Inject(FsmState s, FsmStateAction a)
        {
            try
            {
                var list = new List<FsmStateAction>(s.Actions);
                list.Insert(0, a);
                s.Actions = list.ToArray();
                return true;
            }
            catch { return false; }
        }

        static void OnLocal(Point p, bool install)
        {
            GameObject part = p.Fsm.FsmVariables.GetFsmGameObject("ActivePart").Value;
            if (install) Freshen(part, p.Fsm, IdOf(part));   // ses vis s'activent : accrochees avant le premier tour de cle
            if (!Session.Active || Time.realtimeSinceStartup - loadedAt < 20f) return;   // chargement : chacun le sien
            string id = IdOf(part);
            if (id.Length == 0) { Log.Warn("piece sans ID sur " + p.Key); return; }
            PlayMakerFSM data = Game.FsmOn(part, "Data");
            FsmInt aid = data != null ? data.FsmVariables.FindFsmInt("AssemblyID") : null;
            Log.Info((install ? "montage : " : "demontage : ") + id + " sur " + p.Key);
            Session.SendAll(new NetWriter(Msg.Part).U8(Session.LocalId).Bool(install).Str(p.Key).Str(id).I32(aid != null ? aid.Value : 0)
                .Vec(part.transform.position).Quat(part.transform.rotation), true);
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            bool install = r.Bool();
            string key = r.Str(), id = r.Str();
            int aid = r.I32();
            Vector3 pos = r.Vec();
            Quaternion rot = r.Quat();
            if (Session.IsHost)
                Session.Broadcast(new NetWriter(Msg.Part).U8(who).Bool(install).Str(key).Str(id).I32(aid).Vec(pos).Quat(rot), true, who);
            Point p;
            if (!points.TryGetValue(key, out p) || p.Fsm == null) { Scan(); points.TryGetValue(key, out p); }
            GameObject part = FindByIdCached(id);
            if (part == null) part = FindById(id);   // (achetee depuis le dernier releve des ID)
            if (p == null || part == null) { Log.Warn("piece " + id + " / point " + key + " introuvable ici"); return; }
            PlayMakerFSM data = Game.FsmOn(part, "Data");
            bool isIn = Holds(p.Fsm, part);
            if (install == isIn) { Log.Info("piece " + id + " deja " + (isIn ? "montee" : "demontee")); return; }
            p.Fsm.FsmVariables.GetFsmGameObject("ActivePart").Value = part;
            Trace.Watch(p.Fsm, "point " + key, 8f);
            Trace.Watch(data, "piece " + id, 8f);
            applying = true; Replay.Depth++;
            try
            {
                if (install)
                {
                    FsmInt a = data.FsmVariables.FindFsmInt("AssemblyID");
                    if (a != null) a.Value = aid;
                    // La piece exactement ou le joueur l'a posee (le jeu la fixe la ou elle est).
                    part.transform.position = pos;
                    part.transform.rotation = rot;
                    data.SendEvent("INSTALL");
                    Game.SetState(p.Fsm, "Install 2");
                }
                else Game.SetState(p.Fsm, "Remove part");
            }
            finally { applying = false; Replay.Depth--; }
            if (install) Freshen(part, p.Fsm, id);   // ses vis, avant les tours de cle de l'autre
            Log.Info("piece " + id + (install ? " montee sur " : " demontee de ") + key + " (joueur #" + who + ")");
        }

        // Montee = le point de montage la tient (sa variable Installed + ActivePart). La variable
        // Installed de la piece elle-meme n'est pas tenue a jour par toutes les pieces.
        static bool Holds(PlayMakerFSM point, GameObject part)
        {
            FsmBool inst = point.FsmVariables.FindFsmBool("Installed");
            return inst != null && inst.Value && point.FsmVariables.GetFsmGameObject("ActivePart").Value == part;
        }

        // ID -> objet, reconstruit au besoin (FindById parcourt tous les automates : couteux).
        static readonly Dictionary<string, GameObject> idMap = new Dictionary<string, GameObject>();
        static float idMapAt = -100;

        public static GameObject FindByIdCached(string id)
        {
            GameObject go;
            if (idMap.TryGetValue(id, out go) && go != null) return go;
            if (Time.realtimeSinceStartup - idMapAt < 2f) return null;
            idMapAt = Time.realtimeSinceStartup;
            idMap.Clear();
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var f = (PlayMakerFSM)o;
                if (f.hideFlags != HideFlags.None || f.FsmName != "Data") continue;
                FsmString s = f.FsmVariables.FindFsmString("ID");
                if (s != null && s.Value.Length > 0) idMap[s.Value] = f.gameObject;
            }
            return idMap.TryGetValue(id, out go) ? go : null;
        }

        public static GameObject FindById(string id)
        {
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var f = (PlayMakerFSM)o;
                if (f.hideFlags != HideFlags.None || f.FsmName != "Data") continue;
                FsmString s = f.FsmVariables.FindFsmString("ID");
                if (s != null && s.Value == id) return f.gameObject;
            }
            return null;
        }

        // Essais : monte (ou demonte) la piece 'id' sur son point prevu (InstallPoint), comme le jeu.
        public static string TestToggle(string id)
        {
            GameObject part = FindById(id);
            if (part == null) return "piece introuvable";
            PlayMakerFSM data = Game.FsmOn(part, "Data");
            GameObject ip = data.FsmVariables.GetFsmGameObject("InstallPoint").Value;
            if (ip == null) return "pas de point de montage";
            PlayMakerFSM asm = Game.FsmOn(ip, "Data");
            bool isIn = Holds(asm, part);
            asm.FsmVariables.GetFsmGameObject("ActivePart").Value = part;
            Trace.Watch(asm, "point " + Recon.Path(ip.transform), 8f);
            Trace.Watch(data, "piece " + id, 8f);
            if (isIn) { Game.SetState(asm, "Remove part"); return "demontage de " + id; }
            part.transform.position = ip.transform.position;
            Game.SetState(asm, "Install 1");   // comme le clic : AssemblyID, INSTALL a la piece, puis Install 2
            return "montage de " + id + " sur " + Recon.Path(ip.transform);
        }

        // Essais ([Test] Autotest=boulons, TestPiece = ID d'une piece libre a monter, VIN413C1 par defaut) :
        // l'hote monte la piece a 30 s puis serre tout de suite sa premiere vis de 3 crans (0,5 s, 1 s, 1,5 s
        // apres le montage : avant tout releve periodique) ; a 42 s chacun note les vis de la piece. Attendu chez
        // l'invite : "piece ... montee", trois "vis <ID>/Bolts/BoltPM#k/boltk = 1, 2, 3 (joueur #0)" et le meme
        // etat qu'a l'hote ("...=3"), aucune "vis ... introuvable".
        static int testStep;
        static float testAt;

        public static void Test(string mode, float t)
        {
            if (mode != "boulons") return;
            string id = Config.Get("Test", "TestPiece", "VIN413C1");
            float now = Time.realtimeSinceStartup;
            if (Session.IsHost && t > 30f && testStep == 0) { testStep = 1; testAt = now; Log.Info("autotest : boulons, " + TestToggle(id)); }
            if (Session.IsHost && testStep >= 1 && testStep <= 3 && now - testAt > 0.5f * testStep)
            { testStep++; Log.Info("autotest : boulons, " + (now - testAt).ToString("F1") + " s apres le montage, vis " + TestBolt(id, true)); }
            if (t > 42f && testStep < 10)
            {
                testStep = 10;
                Log.Info("autotest : boulons, " + id + " : " + BoltState(id) + " (" + screws.Count + " vis suivies, " + waiting.Count + " en attente, " + pendingBolts.Count + " recues en attente)");
            }
        }

        // Reconnaissance : chaque automate Data -> objet, chemin, ID, Installed, point de montage.
        public static string DumpIds()
        {
            var sb = new System.Text.StringBuilder();
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var f = (PlayMakerFSM)o;
                if (f.hideFlags != HideFlags.None || f.FsmName != "Data") continue;
                FsmString id = f.FsmVariables.FindFsmString("ID");
                if (id == null) continue;
                FsmBool inst = f.FsmVariables.FindFsmBool("Installed");
                FsmGameObject ip = f.FsmVariables.FindFsmGameObject("InstallPoint");
                sb.Append(Recon.Path(f.transform)).Append(" | ID=").Append(id.Value)
                  .Append(" | installe=").Append(inst != null && inst.Value)
                  .Append(" | point=").Append(ip != null && ip.Value != null ? Recon.Path(ip.Value.transform) : "-")
                  .Append(" | etat=").Append(f.ActiveStateName)
                  .Append(" | actif=").Append(f.gameObject.activeInHierarchy).Append('\n');
            }
            string p = System.IO.Path.Combine(System.IO.Path.Combine(Log.DataDir, "dumps"), "pieces.txt");
            System.IO.File.WriteAllText(p, sb.ToString());
            return p;
        }
    }
}
