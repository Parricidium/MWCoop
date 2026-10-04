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
        static readonly HashSet<PlayMakerFSM> screws = new HashSet<PlayMakerFSM>();

        class BoltHook : ModHook
        {
            public override string Module { get { return "pieces (boulons)"; } }
            public PlayMakerFSM F;
            public override void OnEnter()
            {
                try { if (!applying && Replay.Depth == 0) OnLocalBolt(F); } catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        static string BoltName(PlayMakerFSM f)
        {
            return f.transform.childCount > 0 ? f.transform.GetChild(0).name : f.name;
        }

        static void OnLocalBolt(PlayMakerFSM f)
        {
            if (!Session.Active || Time.realtimeSinceStartup - loadedAt < 20f) return;
            GameObject part = f.FsmVariables.GetFsmGameObject("ThisPart").Value;
            string id = IdOf(part);
            if (id.Length == 0) return;
            int target = f.FsmVariables.GetFsmInt("BoltTightness").Value + f.FsmVariables.GetFsmInt("ScrewInt").Value;
            Session.SendAll(new NetWriter(Msg.Bolt).U8(Session.LocalId).Str(id).Str(BoltName(f)).U8(target), true);
        }

        public static void OnBolt(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            string id = r.Str(), bolt = r.Str();
            int target = r.U8();
            if (Session.IsHost) Session.Broadcast(new NetWriter(Msg.Bolt).U8(who).Str(id).Str(bolt).U8(target), true, who);
            GameObject part = FindById(id);
            PlayMakerFSM f = null;
            foreach (PlayMakerFSM s in screws)
                if (s != null && s.FsmVariables.GetFsmGameObject("ThisPart").Value == part && BoltName(s) == bolt) { f = s; break; }
            if (part == null || f == null) { Log.Warn("vis " + id + "/" + bolt + " introuvable ici"); return; }
            FsmInt t = f.FsmVariables.GetFsmInt("BoltTightness");
            applying = true; Replay.Depth++;
            try
            {
                for (int guard = 0; t.Value != target && guard < 10; guard++)
                    f.SendEvent(t.Value < target ? "TIGHTEN" : "UNTIGHTEN");
            }
            finally { applying = false; Replay.Depth--; }
            if (t.Value != target) Log.Warn("vis " + id + "/" + bolt + " : " + t.Value + " au lieu de " + target);
            else Log.Info("vis " + id + "/" + bolt + " = " + target + " (joueur #" + who + ")");
        }

        static int ScanBolts()
        {
            int n = 0;
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var f = (PlayMakerFSM)o;
                if (f.hideFlags != HideFlags.None || f.FsmName != "Screw" || screws.Contains(f)) continue;
                if (f.FsmVariables.FindFsmGameObject("ThisPart") == null || f.FsmVariables.FindFsmInt("BoltTightness") == null) continue;
                FsmState st = f.Fsm.GetState("Screw");
                if (st == null || !Inject(st, new BoltHook { F = f })) continue;   // inactive : on reessaiera
                screws.Add(f);
                n++;
            }
            return n;
        }

        // Essais : serre (ou desserre) d'un cran la premiere vis de la piece 'id', comme la cle.
        public static string TestBolt(string id, bool tighten)
        {
            GameObject part = FindById(id);
            foreach (PlayMakerFSM s in screws)
                if (s != null && s.FsmVariables.GetFsmGameObject("ThisPart").Value == part)
                {
                    s.SendEvent(tighten ? "TIGHTEN" : "UNTIGHTEN");
                    return BoltName(s) + " = " + s.FsmVariables.GetFsmInt("BoltTightness").Value;
                }
            return "aucune vis active pour " + id;
        }

        public static string BoltState(string id)
        {
            GameObject part = FindById(id);
            var sb = new System.Text.StringBuilder();
            foreach (PlayMakerFSM s in screws)
                if (s != null && s.FsmVariables.GetFsmGameObject("ThisPart").Value == part)
                    sb.Append(BoltName(s)).Append('=').Append(s.FsmVariables.GetFsmInt("BoltTightness").Value).Append(' ');
            PlayMakerFSM d = part != null ? Game.FsmOn(part, "Data") : null;
            FsmFloat tight = d != null ? d.FsmVariables.FindFsmFloat("Tightness") : null;
            return sb + "| serrage piece " + (tight != null ? tight.Value.ToString() : "?");
        }

        public static void OnLevelLoaded()
        {
            screws.Clear();
            points.Clear();
            hooked.Clear();
            loadedAt = Time.realtimeSinceStartup;
            nextScan = PlayerSync.InGame ? loadedAt + 8f : -1;
        }

        public static void Update()
        {
            if (nextScan < 0 || Time.realtimeSinceStartup < nextScan) return;
            nextScan = Time.realtimeSinceStartup + 15f;   // nouvelles pieces (achats...) : nouveau passage
            Scan();
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
            int added = 0;
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var f = (PlayMakerFSM)o;
                if (f.hideFlags != HideFlags.None || f.FsmName != "Data" || hooked.Contains(f)) continue;
                if (!f.name.StartsWith("VINP") && f.Fsm.GetState("Install 2") == null) continue;
                FsmState inst = f.Fsm.GetState("Install 2"), rem = f.Fsm.GetState("Remove part");
                if (inst == null || rem == null || f.FsmVariables.FindFsmGameObject("ActivePart") == null) continue;
                if (f.transform.root.position == Vector3.zero && f.transform.root.name.StartsWith("VIN")) continue;   // modeles
                var p = new Point { Key = KeyOf(f.transform), Fsm = f };
                if (!Inject(inst, new Hook { P = p, Install = true }) || !Inject(rem, new Hook { P = p, Install = false })) continue;
                hooked.Add(f);
                points[p.Key] = p;
                added++;
            }
            int bolts = ScanBolts();
            if (added > 0 || bolts > 0)
                Log.Info("pieces : " + added + " points de montage et " + bolts + " vis de plus (" + points.Count + " points, " + screws.Count + " vis)");
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
            nextScan = Mathf.Min(nextScan, Time.realtimeSinceStartup + 1f);   // les vis de la piece s'activent
            if (!Session.Active || Time.realtimeSinceStartup - loadedAt < 20f) return;   // chargement : chacun le sien
            GameObject part = p.Fsm.FsmVariables.GetFsmGameObject("ActivePart").Value;
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
            GameObject part = FindById(id);
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
            nextScan = Mathf.Min(nextScan, Time.realtimeSinceStartup + 1f);
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
