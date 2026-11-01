using System.Collections.Generic;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Usine (Futufon, JOBS/FACTORY) : le travail visible et partage, comme les autres boulots (JD, 01/11 : « il faut
    // completement syncro le travail a Futufon »). Avant, le travail etait a chacun : les caisses des bacs et les pieces
    // qu'elles creent n'existaient que chez celui qui travaillait, l'autre le voyait seulement « jeter des objets par terre »
    // (Донской, 01/11).
    // Travail du jeu (releve du 01/11) : cinq bacs (JOB/PickChargers, PickManuals, PickSheets, PickTrays, PickBoxes, automate
    // 'Use') ; « Pick BOX » sort la caisse unique du bac (variable Package : chargers box(Clone)...) et la met en main. Dans
    // la caisse (automate 'Use' ; 'Data' pour la caisse a colis), « Open box » l'ouvre ; « Pick item » (chargeurs, notices) ou
    // « State 8 » (plateaux, feuilles) cree une piece (CreateObject depuis LOD1/Hidden) et decompte Items/Pos. Puis :
    //  - chargeur et notice poses dans le plateau (plastic tray(Clone)/PivotCharger/TriggerCharger, PivotManual/TriggerManual,
    //    'Assembly' : Assemble -> State 2, SetParent, le Contents de la piece lui retire son corps) ;
    //  - feuille pliee en colis (package(Clone) 'Use' : « State 1 » = un pli, Stage 1 a 7) ; a l'etape 6, le plateau pose dedans
    //    (package_stage6/TriggerTray 'Assembly') ;
    //  - colis dans la caisse a colis (packages box(Clone)/TriggerPackage 'Assembly' : Check contents le detruit, 4 places) ;
    //  - caisse pleine sur une palette du joueur (PalletPackagesPlayer/TriggerBox 'Assembly' : Insert box ajoute au compte de
    //    JOB::JobProgress, PackagesTotal).
    // Ici, ces automates sont a ce module (ni recopies ni rejoues par le monde) :
    //  - chaque caisse est suivie par Props sous une cle fixe (usine:<bac>) ; chaque palette du joueur : usine:palette:<rang> ;
    //  - « Pick BOX » chez l'un : la caisse est allumee et montree dans la main de celui-la chez les autres ;
    //  - etats de la caisse (Open box, State 1, State 3 et ceux qui creent) rejoues sur la meme caisse chez les autres ; chaque
    //    piece creee recoit une cle reseau (usine:<joueur>:<n>), donnee a la piece creee par le rejeu (memes compteurs, meme
    //    piece) : Props suit ensuite sa pose et qui la tient ;
    //  - chaque montage (Assemble) part avec le conteneur, le declencheur et la piece (cles) et est rejoue avec la meme piece ;
    //    chaque pli du colis aussi ;
    //  - le compte de colis (JobProgress) reste a celui qui travaille : chez les autres, une palette remplie par le rejeu
    //    remet leur compte comme avant (chacun sa paie, regle des autres boulots ; Wallet, usine.@paquets).
    // Jobs (@usine) : U8 joueur, Str "@usine", U8 genre, puis :
    //  1 caisse sortie du bac : U8 bac ; 2 etat de la caisse : U8 bac, Str etat, Str cle de la piece creee ("" : aucune) ;
    //  3 montage : Str conteneur, Str chemin du declencheur, Str piece ; 4 pli du colis : Str colis.
    // [Test] UsineAncienne=1 : comme avant (travail a chacun).
    public static class Usine
    {
        const string Mod = "usine";
        const string Root = "JOBS/FACTORY/OpeningTimes/LOD1/JOB";
        static readonly string[] Bins = { "PickChargers", "PickManuals", "PickSheets", "PickTrays", "PickBoxes" };
        static readonly string[] BoxStates = { "Open box", "State 1", "State 3" };

        class Bin { public int Index; public PlayMakerFSM Use, Box; public GameObject BoxGo; public bool Hooked, BoxHooked; public string LastMade; }
        static readonly Bin[] bins = new Bin[5];
        static readonly Dictionary<string, GameObject> byKey = new Dictionary<string, GameObject>();
        static readonly Dictionary<GameObject, string> keyOf = new Dictionary<GameObject, string>();
        static readonly List<GameObject> toHook = new List<GameObject>();
        static readonly HashSet<PlayMakerFSM> hooked = new HashSet<PlayMakerFSM>();
        static readonly Dictionary<PlayMakerFSM, int[]> palletRestore = new Dictionary<PlayMakerFSM, int[]>();
        static float nextScan = -1, nextHook;
        static int made;
        static bool palletsFound;
        static string pendingKey;   // (rejeu d'un etat qui cree : la cle de la piece que l'autre a creee)

        public static bool On { get { return !Game.Msc && Config.GetInt("Test", "UsineAncienne", 0) == 0; } }

        public static void OnLevelLoaded()
        {
            for (int i = 0; i < bins.Length; i++) bins[i] = null;
            byKey.Clear(); keyOf.Clear(); toHook.Clear(); hooked.Clear(); palletRestore.Clear();
            nextScan = PlayerSync.InGame ? Time.realtimeSinceStartup + 8f : -1;   // (avant le releve du monde, 16 s)
            made = 0; pendingKey = null; palletsFound = false; tStep = 0; tBoxOut = false;
        }

        static void Name(GameObject go, string key)
        {
            if (go == null) return;
            byKey[key] = go; keyOf[go] = key;
            if (!toHook.Contains(go)) toHook.Add(go);
        }

        // ---------------------------------------------------------------- crochets
        class Hook : ModHook
        {
            public override string Module { get { return Mod; } }
            public Bin B; public string State; public int Kind;   // 1 sortie du bac, 2 etat de la caisse (apres la creation si elle cree)
            public override void OnEnter()
            {
                try { if (Session.Active && Replay.Depth == 0) OnLocal(this); else if (Kind == 2 && Replay.Depth > 0) OnReplayed(this); }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }
        class AsmHook : ModHook
        {
            public override string Module { get { return Mod; } }
            public PlayMakerFSM F;
            public override void OnEnter()
            {
                try { if (Session.Active && Replay.Depth == 0) OnAssemble(F); }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }
        class FoldHook : ModHook
        {
            public override string Module { get { return Mod; } }
            public PlayMakerFSM F;
            public override void OnEnter()
            {
                try { if (Session.Active && Replay.Depth == 0) OnFold(F); }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }
        // (palette remplie par le rejeu d'un montage d'un autre : juste apres les AddToFsmInt d'Insert box, le compte d'ici remis)
        class CountHook : ModHook
        {
            public override string Module { get { return Mod; } }
            public PlayMakerFSM F;
            public override void OnEnter()
            {
                try
                {
                    int[] was;
                    if (palletRestore.TryGetValue(F, out was)) { palletRestore.Remove(F); SetCounts(was); MWCoop.Log.Info("usine : palette remplie par un autre, compte de colis d'ici garde (" + was[0] + " / " + was[1] + ")"); }
                }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        static PlayMakerFSM JobProgress() { GameObject j = Game.FindAny(Root); return j != null ? Game.FsmOn(j, "JobProgress") : null; }
        static int[] Counts()
        {
            PlayMakerFSM jp = JobProgress();
            FsmInt t = jp != null ? jp.FsmVariables.FindFsmInt("PackagesTotal") : null, e = jp != null ? jp.FsmVariables.FindFsmInt("PackagesEmpty") : null;
            return new[] { t != null ? t.Value : 0, e != null ? e.Value : 0 };
        }
        static void SetCounts(int[] v)
        {
            PlayMakerFSM jp = JobProgress();
            if (jp == null) return;
            FsmInt t = jp.FsmVariables.FindFsmInt("PackagesTotal"), e = jp.FsmVariables.FindFsmInt("PackagesEmpty");
            if (t != null) t.Value = v[0];
            if (e != null) e.Value = v[1];
        }

        // ---------------------------------------------------------------- releve
        public static void Update()
        {
            if (!On || nextScan < 0 || !Session.Active) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextHook) { nextHook = now + 0.5f; HookPending(); }
            if (now < nextScan) return;
            bool all = true;
            for (int i = 0; i < Bins.Length; i++)
            {
                if (bins[i] == null || !bins[i].Hooked) { if (!TryBin(i)) { all = false; continue; } }
                if (!bins[i].BoxHooked && !HookBox(bins[i])) all = false;
            }
            if (!palletsFound) palletsFound = FindPallets();
            nextScan = now + (all && palletsFound ? 30f : 1f);   // (caisse eteinte au bac : son automate n'est pret qu'une fois sortie)
        }

        static bool FindPallets()
        {
            GameObject job = Game.FindAny(Root);
            if (job == null) return false;
            int n = 0;
            for (int i = 0; i < job.transform.childCount; i++)
            {
                Transform c = job.transform.GetChild(i);
                if (c.name != "PalletPackagesPlayer") continue;
                Name(c.gameObject, "usine:palette:" + n);
                n++;
            }
            if (n > 0) Log.Info("usine : " + n + " palettes du joueur suivies");
            return n > 0;
        }

        static bool TryBin(int i)
        {
            GameObject bg = Game.FindAny(Root + "/" + Bins[i]);
            PlayMakerFSM use = bg != null ? Game.FsmOn(bg, "Use") : null;
            if (use == null) return false;
            FsmGameObject pv = use.FsmVariables.FindFsmGameObject("Package");
            GameObject box = pv != null ? pv.Value : null;
            if (box == null) return false;
            PlayMakerFSM bu = Game.FsmOn(box, "Use") ?? Game.FsmOn(box, "Data");
            if (use.Fsm == null || !use.Fsm.Initialized) return false;
            var b = bins[i] ?? (bins[i] = new Bin { Index = i });
            b.Use = use; b.Box = bu; b.BoxGo = box;
            if (!Replay.Claim(use, Mod)) Log.Warn("usine : " + Bins[i] + "::Use deja pris par " + Replay.Owner(use));
            if (bu != null && !Replay.Claim(bu, Mod)) Log.Warn("usine : " + box.name + "::" + bu.FsmName + " deja pris par " + Replay.Owner(bu));
            FsmState pick = use.Fsm.GetState("Pick BOX");
            if (pick != null) FsmHooks.Prepend(pick, new Hook { B = b, State = "Pick BOX", Kind = 1 });
            Props.Adopt(box, "usine:" + Bins[i]);
            Name(box, "usine:" + Bins[i]);
            b.Hooked = true;
            Log.Info("usine : bac " + Bins[i] + " suivi (caisse " + box.name + (box.activeInHierarchy ? "" : ", eteinte") + ")");
            return true;
        }

        // Etats de la caisse accroches (une fois son automate pret : caisse allumee au moins une fois).
        static bool HookBox(Bin b)
        {
            if (b.Box == null || b.Box.FsmName != "Use") { b.BoxHooked = true; return true; }   // (caisse a colis : 'Data', son declencheur par toHook)
            if (b.Box.Fsm == null || !b.Box.Fsm.Initialized) return false;
            int n = 0;
            foreach (FsmState st in b.Box.Fsm.States)
            {
                // (etat qui cree : juste apres le CreateObject -- l'IntCompare de la fin quitte l'etat, une action ajoutee en dernier ne
                // tournait pas)
                int at = -1;
                for (int k = 0; st.Actions != null && k < st.Actions.Length; k++) if (st.Actions[k] is CreateObject) { at = k + 1; break; }
                if (at >= 0) { FsmHooks.Insert(st, at, new Hook { B = b, State = st.Name, Kind = 2 }); n++; continue; }
                if (System.Array.IndexOf(BoxStates, st.Name) >= 0) { FsmHooks.Prepend(st, new Hook { B = b, State = st.Name, Kind = 2 }); n++; }
            }
            b.BoxHooked = true;
            Log.Info("usine : caisse " + b.BoxGo.name + " accrochee (" + n + " etats rejoues)");
            return true;
        }

        // Montages et plis des pieces et conteneurs nommes (automate pret : piece allumee ; le declencheur du plateau dans le colis
        // n'est pret qu'a l'etape 6).
        static void HookPending()
        {
            for (int i = toHook.Count - 1; i >= 0; i--)
            {
                GameObject go = toHook[i];
                if (go == null) { toHook.RemoveAt(i); continue; }
                bool left = false;
                foreach (PlayMakerFSM f in go.GetComponentsInChildren<PlayMakerFSM>(true))
                {
                    if (hooked.Contains(f)) continue;
                    bool asm = f.FsmName == "Assembly", fold = f.FsmName == "Use" && f.gameObject == go && go.name.StartsWith("package(");
                    if (!asm && !fold) continue;
                    if (f.Fsm == null || !f.Fsm.Initialized) { left = true; continue; }
                    if (!Replay.Claim(f, Mod)) { Log.Warn("usine : " + Recon.Path(f.transform) + "::" + f.FsmName + " deja pris par " + Replay.Owner(f)); hooked.Add(f); continue; }
                    if (asm)
                    {
                        FsmState st = f.Fsm.GetState("Assemble");
                        if (st != null) FsmHooks.Prepend(st, new AsmHook { F = f });
                        FsmState ins = f.Fsm.GetState("Insert box");
                        if (ins != null && go.name == "PalletPackagesPlayer")
                        {
                            int at = -1;
                            for (int k = 0; k < ins.Actions.Length; k++) if (ins.Actions[k] is AddToFsmInt) at = k + 1;
                            if (at >= 0) FsmHooks.Insert(ins, at, new CountHook { F = f });
                        }
                    }
                    else { FsmState st = f.Fsm.GetState("State 1"); if (st != null) FsmHooks.Prepend(st, new FoldHook { F = f }); }
                    hooked.Add(f);
                }
                if (!left) toHook.RemoveAt(i);
            }
        }

        static GameObject Created(PlayMakerFSM f, string state)
        {
            FsmState st = f.Fsm.GetState(state);
            if (st == null || st.Actions == null) return null;
            foreach (FsmStateAction a in st.Actions)
            {
                var c = a as CreateObject;
                if (c != null && c.storeObject != null && c.storeObject.Value != null) return c.storeObject.Value;
            }
            return null;
        }

        static string RelPath(Transform from, Transform to)
        {
            var parts = new List<string>();
            for (Transform t = to; t != null && t != from; t = t.parent) parts.Insert(0, t.name);
            return string.Join("/", parts.ToArray());
        }

        // ---------------------------------------------------------------- ici
        static void OnLocal(Hook h)
        {
            var w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@usine");
            if (h.Kind == 1)
            {
                Session.SendAll(w.U8(1).U8(h.B.Index), true);
                Log.Info("usine : caisse de " + Bins[h.B.Index] + " sortie ici, envoye");
                return;
            }
            string key = "";
            GameObject go = Created(h.B.Box, h.State);
            if (go != null) { key = "usine:" + Session.LocalId + ":" + (++made); Props.Adopt(go, key); Name(go, key); h.B.LastMade = key; }
            Session.SendAll(w.U8(2).U8(h.B.Index).Str(h.State).Str(key), true);
            Log.Info("usine : " + Bins[h.B.Index] + ", caisse -> " + h.State + " ici" + (key.Length > 0 ? " (piece " + key + ", " + go.name + ")" : "") + ", envoye");
        }

        static void OnReplayed(Hook h)
        {
            if (pendingKey == null) return;
            GameObject go = Created(h.B.Box, h.State);
            if (go != null) { Props.Adopt(go, pendingKey); Name(go, pendingKey); h.B.LastMade = pendingKey; }
            Log.Info("usine : piece " + pendingKey + (go != null ? " creee ici (" + go.name + ")" : " : rien de cree ici"));
            pendingKey = null;
        }

        static void OnAssemble(PlayMakerFSM f)
        {
            FsmGameObject pv = f.FsmVariables.FindFsmGameObject("Part");
            GameObject part = pv != null ? pv.Value : null;
            string pk = part != null ? (keyOf.ContainsKey(part) ? keyOf[part] : Props.KeyOf(part)) : null;
            string ck = null, rel = null;
            for (Transform t = f.transform; t != null; t = t.parent)
            {
                string k;
                if (keyOf.TryGetValue(t.gameObject, out k)) { ck = k; rel = RelPath(t, f.transform); break; }
            }
            if (ck == null || string.IsNullOrEmpty(pk)) { Log.Warn("usine : montage " + Recon.Path(f.transform) + " de " + (part != null ? part.name : "?") + " pas envoye (" + (ck == null ? "conteneur" : "piece") + " sans cle)"); return; }
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@usine").U8(3).Str(ck).Str(rel).Str(pk), true);
            Log.Info("usine : montage de " + pk + " dans " + ck + "/" + rel + " ici, envoye");
        }

        static void OnFold(PlayMakerFSM f)
        {
            string k;
            if (!keyOf.TryGetValue(f.gameObject, out k)) return;
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@usine").U8(4).Str(k), true);
        }

        // ---------------------------------------------------------------- recu
        public static void OnMessage(int who, NetReader r)
        {
            int kind = r.U8();
            if (kind == 1 || kind == 2)
            {
                int bi = r.U8();
                string state = kind == 2 ? r.Str() : "", key = kind == 2 ? r.Str() : "";
                if (!On || bi >= bins.Length) return;
                Bin b = bins[bi];
                if (b == null || !b.Hooked) { TryBin(bi); b = bins[bi]; }
                if (b == null || !b.Hooked) { Log.Warn("usine : bac " + (bi < Bins.Length ? Bins[bi] : "?") + " pas encore trouve ici (#" + who + ")"); return; }
                if (!b.BoxGo.activeSelf) b.BoxGo.SetActive(true);
                if (!b.BoxHooked) HookBox(b);
                if (kind == 1) { Props.HoldFromRemote(b.BoxGo, who); Log.Info("usine : caisse de " + Bins[bi] + " sortie par #" + who + ", en main chez lui"); return; }
                if (b.Box == null || b.Box.Fsm.GetState(state) == null) return;
                pendingKey = key.Length > 0 ? key : null;
                Replay.Depth++;
                try { Game.SetState(b.Box, state); }
                finally { Replay.Depth--; pendingKey = null; }
                Log.Info("usine : " + Bins[bi] + ", caisse -> " + state + " par #" + who + " (etat " + b.Box.ActiveStateName + ")");
                return;
            }
            if (kind == 3)
            {
                string ck = r.Str(), rel = r.Str(), pk = r.Str();
                if (!On) return;
                GameObject c, part;
                if (!byKey.TryGetValue(ck, out c) || c == null) { Log.Warn("usine : montage de #" + who + " : conteneur " + ck + " introuvable ici"); return; }
                if (!byKey.TryGetValue(pk, out part) || part == null) part = Props.ObjectOf(pk);
                Transform tr = rel.Length > 0 ? c.transform.Find(rel) : c.transform;
                PlayMakerFSM asm = tr != null ? Game.FsmOn(tr.gameObject, "Assembly") : null;
                if (asm == null || part == null) { Log.Warn("usine : montage de #" + who + " : " + (asm == null ? "declencheur " + ck + "/" + rel : "piece " + pk) + " introuvable ici"); return; }
                if (!c.activeSelf) c.SetActive(true);   // (le conteneur est allume chez celui qui monte ; ses enfants suivent l'etat du jeu : etape du colis)
                if (!hooked.Contains(asm)) HookPending();
                FsmGameObject pv = asm.FsmVariables.FindFsmGameObject("Part");
                if (pv != null) pv.Value = part;
                Props.ReleaseRemote(part);
                if (c.name == "PalletPackagesPlayer") palletRestore[asm] = Counts();
                Replay.Depth++;
                try { Game.SetState(asm, "Assemble"); }
                finally { Replay.Depth--; }
                Log.Info("usine : montage de " + pk + " dans " + ck + "/" + rel + " par #" + who + " (etat " + asm.ActiveStateName + ")");
                return;
            }
            if (kind == 4)
            {
                string k = r.Str();
                GameObject p;
                if (!On || !byKey.TryGetValue(k, out p) || p == null) return;
                PlayMakerFSM use = Game.FsmOn(p, "Use");
                if (use == null) return;
                Replay.Depth++;
                try { Game.SetState(use, "State 1"); }
                finally { Replay.Depth--; }
                FsmInt stage = use.FsmVariables.FindFsmInt("Stage");
                Log.Info("usine : colis " + k + " plie par #" + who + " (etape " + (stage != null ? stage.Value : -1) + ")");
            }
        }

        // ---------------------------------------------------------------- essais
        // [Test] Autotest=usinetravail (2 joueurs, TestPos dans l'usine : 2172,0.5,-470) : 30 s apres l'arrivee de l'autre, le joueur
        // [Test] UsineActeur (invite par defaut) fait un colis complet a coups d'etats du jeu : un plateau (+0), un chargeur (+2/+3),
        // une notice (+4/+5) ; chargeur puis notice dans le plateau (+7, +9) ; une feuille (+11), pliee 6 fois (+13 a +18) ;
        // plateau dans le colis (+20), 7e pli (+22) ; colis dans la caisse a colis (+24) ; caisse sur la 1re palette (+30).
        // Chacun note toutes les 3 s le plateau, le colis, la caisse a colis, la palette et le compte de colis (JobProgress).
        static int tStep; static float tAt, tLog; static bool tBoxOut;
        static void Act(int bi, string state)
        {
            Bin b = bins[bi];
            if (b == null || b.Box == null) { Log.Info("autotest : usine, " + Bins[bi] + " absent"); return; }
            if (!b.BoxGo.activeSelf) b.BoxGo.SetActive(true);
            if (!b.BoxHooked) HookBox(b);
            Game.SetState(b.Box, state);
        }
        static string Last(int bi) { return bins[bi] != null ? bins[bi].LastMade : null; }
        static void TestAssemble(string ck, string rel, string pk)
        {
            GameObject c, part;
            byKey.TryGetValue(ck ?? "", out c); byKey.TryGetValue(pk ?? "", out part);
            Transform tr = c != null ? (rel.Length > 0 ? c.transform.Find(rel) : c.transform) : null;
            PlayMakerFSM asm = tr != null ? Game.FsmOn(tr.gameObject, "Assembly") : null;
            if (asm == null || part == null) { Log.Info("autotest : usine, montage " + ck + "/" + rel + " de " + pk + " impossible (" + (asm == null ? "declencheur" : "piece") + ")"); return; }
            if (!hooked.Contains(asm)) HookPending();
            asm.FsmVariables.GetFsmGameObject("Part").Value = part;
            Game.SetState(asm, "Assemble");
            Log.Info("autotest : usine, montage " + ck + "/" + rel + " de " + pk + " -> " + asm.ActiveStateName);
        }
        static void Fold(string pk)
        {
            GameObject p;
            if (pk == null || !byKey.TryGetValue(pk, out p) || p == null) return;
            PlayMakerFSM use = Game.FsmOn(p, "Use");
            if (use != null) Game.SetState(use, "State 1");
        }
        static string Vars(GameObject go, string fsm, params string[] names)
        {
            PlayMakerFSM f = go != null ? Game.FsmOn(go, fsm) : null;
            if (f == null) return "?";
            var sb = new System.Text.StringBuilder();
            foreach (string n in names)
            {
                FsmInt i = f.FsmVariables.FindFsmInt(n); FsmBool b = f.FsmVariables.FindFsmBool(n);
                sb.Append(n).Append('=').Append(i != null ? i.Value.ToString() : b != null ? b.Value.ToString() : "?").Append(' ');
            }
            return sb.ToString().TrimEnd();
        }
        static string Workers()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 1; i <= 4; i++)
            {
                GameObject c = Game.FindAny(Root + "/WORKER" + i + "/WorkerPIVOT/Worker" + (i == 3 ? "3" : i.ToString()) + "/PhysicsPivot/Pivot/Char");
                GameObject fw = Game.FindAny(Root + "/WORKER" + i + "/WorkerPIVOT/FunctionsWorker");
                PlayMakerFSM pk = fw != null ? Game.FsmOn(fw, "Packaging") : null;
                sb.Append(' ').Append(i).Append(':').Append(c != null ? c.transform.position.ToString("F1") : "?").Append(pk != null ? (pk.enabled ? "" : "(coupe)") + pk.ActiveStateName : "");
            }
            return sb.ToString();
        }
        public static void Test(string mode, float t)
        {
            if (mode != "usinetravail") return;
            float now = Time.realtimeSinceStartup;
            if (tStep == 0 && Jobs.OtherInGame(30f))
            {
                tStep = 1; tAt = now + 2f;
                // (caisses allumees d'abord : l'automate d'une caisse allumee demarre a l'image suivante et ecrasait un etat pose tout de suite)
                if (Session.IsHost == (Config.Get("Test", "UsineActeur", "invite") == "hote")) foreach (Bin bx in bins) if (bx != null && bx.BoxGo != null && !bx.BoxGo.activeSelf) bx.BoxGo.SetActive(true);
            }
            if (now < tAt) return;
            if (tStep == 0) return;
            float d = now - tAt;
            bool actor = Session.IsHost == (Config.Get("Test", "UsineActeur", "invite") == "hote");
            if (actor)
            {
                if (tStep == 1) { tStep++; Act(3, "State 8"); }
                if (tStep == 2 && d > 2f) { tStep++; Act(0, "Check open"); }
                if (tStep == 3 && d > 3f) { tStep++; Act(0, "Check open"); }
                if (tStep == 4 && d > 4f) { tStep++; Act(1, "Check open"); }
                if (tStep == 5 && d > 5f) { tStep++; Act(1, "Check open"); }
                if (tStep == 6 && d > 7f) { tStep++; TestAssemble(Last(3), "PivotCharger/TriggerCharger", Last(0)); }
                if (tStep == 7 && d > 9f) { tStep++; TestAssemble(Last(3), "PivotManual/TriggerManual", Last(1)); }
                if (tStep == 8 && d > 11f) { tStep++; Act(2, "State 8"); }
                if (tStep >= 9 && tStep <= 14 && d > 13f + (tStep - 9)) { tStep++; Fold(Last(2)); }
                if (tStep == 15 && d > 20f) { tStep++; TestAssemble(Last(2), "package_stage6/TriggerTray", Last(3)); }
                if (tStep == 16 && d > 22f) { tStep++; Fold(Last(2)); }
                if (tStep == 17 && d > 23f && !tBoxOut && bins[4] != null) { tBoxOut = true; Game.SetState(bins[4].Use, "Pick BOX"); }   // (caisse a colis sortie du bac)
                if (tStep == 17 && d > 24f) { tStep++; TestAssemble("usine:PickBoxes", "TriggerPackage", Last(2)); }
                if (tStep == 18 && d > 30f) { tStep++; TestAssemble("usine:palette:0", "TriggerBox", "usine:PickBoxes"); }
            }
            if (now >= tLog && d < 45f)
            {
                tLog = now + 3f;
                GameObject tray = null, pack = null, pal = null, pbox = null;
                if (Last(3) != null) byKey.TryGetValue(Last(3), out tray);
                if (Last(2) != null) byKey.TryGetValue(Last(2), out pack);
                byKey.TryGetValue("usine:palette:0", out pal);
                byKey.TryGetValue("usine:PickBoxes", out pbox);
                Transform ch = tray != null ? tray.transform.Find("PivotCharger") : null;
                Log.Info("autotest : usine t=" + d.ToString("F0") + " : plateau " + (tray != null ? Vars(tray, "Contents", "Chager", "Manual") + " (pieces dedans " + (ch != null ? ch.childCount.ToString() : "?") + ")" : "-")
                         + " ; colis " + (pack != null ? Vars(pack, "Use", "Stage", "Mould") : "-")
                         + " ; caisse " + (pbox != null ? Vars(pbox.transform.Find("TriggerPackage") != null ? pbox.transform.Find("TriggerPackage").gameObject : null, "Assembly", "Slot", "TotalPackages") : "-")
                         + " ; palette " + (pal != null ? Vars(pal.transform.Find("TriggerBox") != null ? pal.transform.Find("TriggerBox").gameObject : null, "Assembly", "Slot") : "-")
                         + " ; colis comptes " + Counts()[0] + " ; ouvriers" + Workers());
            }
        }
    }
}
