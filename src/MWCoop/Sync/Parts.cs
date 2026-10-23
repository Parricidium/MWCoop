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
    public static partial class Parts   // (+ PartsMsc.cs : My Summer Car)
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
                try
                {
                    if (!applying && Replay.Depth == 0)
                    {
                        // Demontage par casse (« Update 2 » -BREAKOFF-> « Remove part » : choc, physique emballee) ou a la main
                        // (« Allow removal? » -PROCEED->) : la casse n'est envoyee que par celui qui fait autorite sur la voiture,
                        // et au compte-gouttes (voir OnLocal).
                        FsmTransition tr = P.Fsm.Fsm.LastTransition;
                        OnLocal(P, Install, !Install && (testBreak || tr != null && tr.EventName == "BREAKOFF"));
                    }
                }
                catch (System.Exception e) { Replay.HookError(e); }
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
            if (Game.Msc) return MscConsider(f);
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
            Touch(H(part + "|" + b.Key));
            int target = b.F.FsmVariables.GetFsmInt("BoltTightness").Value + b.F.FsmVariables.GetFsmInt("ScrewInt").Value;
            Session.SendAll(new NetWriter(Msg.Bolt).U8(Session.LocalId).Str(part).Str(b.Key).U8(Mathf.Clamp(target, 0, 255)), true);
        }

        public static void OnBolt(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            string part = r.Str(), key = r.Str();
            int target = r.U8();
            Touch(H(part + "|" + key));
            if (Session.IsHost) Session.Broadcast(new NetWriter(Msg.Bolt).U8(who).Str(part).Str(key).U8(target), true, who);
            // Deja des tours en attente pour cette vis : celui-ci passe apres eux (meme ordre que chez l'autre).
            bool queued = Queued(part, key, pendingBolts.Count);
            if (!queued && ApplyBolt(who, part, key, target, false)) return;
            if (!queued) { CheckWaiting(); if (ApplyBolt(who, part, key, target, false)) return; }
            pendingBolts.Add(new PendingBolt { Who = who, Part = part, Key = key, Target = target, Until = Time.realtimeSinceStartup + 15f });
        }

        // Un tour de la meme vis attend-il deja (parmi les 'upTo' premiers) ?
        static bool Queued(string part, string key, int upTo)
        {
            for (int i = 0; i < upTo; i++)
            {
                PendingBolt pb = pendingBolts[i];
                if (pb != null && pb.Key == key && pb.Part == part) return true;
            }
            return false;
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
            if (Game.Msc) return MscApplyBolt(who, part, key, target, last);
            Bolt b = FindBolt(part, key);
            // (vis inactive : moteur pendu au palan pas encore a 5 cm de son detecteur ici -- en attente, comme absente)
            if (b == null || !b.F.gameObject.activeInHierarchy)
            {
                if (last) Log.Warn("vis " + part + "/" + key + (b == null ? " introuvable ici (piece pas montee ici ?)" : " inactive ici (detecteur du point pas atteint ?)"));
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
            if (t.Value != target)
            {
                if (last) Log.Warn("vis " + part + "/" + key + " : " + t.Value + " au lieu de " + target);
                return false;   // (automate occupe : en attente, reessayee -- relecture du 15/10)
            }
            Log.Info("vis " + part + "/" + key + " = " + target + " (joueur #" + who + ")");
            SyncTightness(part);
            return true;
        }

        // Serrage de la piece = somme de ses vis (le jeu l'additionne cran par cran ; recu en absolu au montage du moteur, les
        // vis rejouees ensuite le comptaient deux fois : piece indemontable chez le receveur -- relecture du 15/10).
        static void SyncTightness(string partKey)
        {
            GameObject part = partKey.StartsWith("p:") ? null : FindByIdCached(partKey);
            PlayMakerFSM data = part != null ? Game.FsmOn(part, "Data") : null;
            FsmFloat tf = data != null ? data.FsmVariables.FindFsmFloat("Tightness") : null;
            if (tf == null) return;
            float sum = 0f; int n = 0;
            foreach (Bolt b in bolts)
                if (b.F != null && PartKey(b.F.FsmVariables.GetFsmGameObject("ThisPart").Value) == partKey) { sum += b.F.FsmVariables.GetFsmInt("BoltTightness").Value; n++; }
            if (n > 0 && Mathf.Abs(tf.Value - sum) > 0.01f) tf.Value = sum;
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
            nextDigest = 0f; touched.Clear(); asked.Clear(); pointsByHash = null; boltsByHash = null; mscPointsByHash = null; ecartDone = false;   // (controle periodique)
            MscReset();
            idMap.Clear(); idMapAt = -100;
            bursts.Clear(); reinstall.Clear(); reinstallLogs = 0;
            testStep = testLogs = 0; test2Step = test2Logs = 0;
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
            if (Game.Msc) MscTick();   // (demontages « echange » faits ici : envoyes avec la cle de la nouvelle piece libre)
            if (mounting.Count > 0) CheckMounting(now);
            if (reinstall.Count > 0) CheckReinstall(now);
            Digest(now); if (!Game.Msc) TestEcart(now);   // (controle periodique de l'hote, 23/10)
            if (now < nextScan) return;
            nextScan = now + 15f;   // nouvelles pieces (achats...) : nouveau passage
            Scan();
        }

        // Automates en attente dont l'objet vient de s'activer.
        static void CheckWaiting()
        {
            if (Game.Msc) { MscCheckWaiting(); return; }
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
                if (f.FsmName == "Screw" || f.FsmName == "Data" || Game.Msc && (f.FsmName == "Removal" || f.FsmName == "Assembly")) n += Consider(f);
            return n;
        }

        // Dans l'ordre d'arrivee, vis par vis : un tour reste derriere un tour plus ancien de la meme vis
        // encore en attente ; une vis jamais trouvee (piece pas montee ici) ne retient pas les autres.
        static void RetryBolts(float now)
        {
            if (now < nextRetry) return;
            nextRetry = now + 0.1f;
            for (int i = 0; i < pendingBolts.Count; i++)
            {
                PendingBolt pb = pendingBolts[i];
                if (Queued(pb.Part, pb.Key, i)) continue;
                bool last = now > pb.Until;
                if (ApplyBolt(pb.Who, pb.Part, pb.Key, pb.Target, last) || last) pendingBolts[i] = null;
            }
            int n = 0;
            for (int i = 0; i < pendingBolts.Count; i++) if (pendingBolts[i] != null) pendingBolts[n++] = pendingBolts[i];
            pendingBolts.RemoveRange(n, pendingBolts.Count - n);
        }
        static float nextRetry;

        // Montage fait (ici ou rejoue) : vis et points de la piece et du point revus, commandes de la voiture cherchees.
        static void Freshen(GameObject part, PlayMakerFSM point, string id)
        {
            float now = Time.realtimeSinceStartup;
            fresh.Add(new Fresh { A = part != null ? part.transform : null, B = point != null ? point.transform : null, Id = id, Until = now + 3f, Next = now + 0.1f });
            Jobs.SoonScan();
        }

        // Essais : etats et actions de l'automate d'un point de montage (et de la piece qui y est, Data), par sa cle.
        public static string DumpPoint(string key)
        {
            Point p;
            if (!points.TryGetValue(key, out p) || p.Fsm == null) return "point " + key + " inconnu (" + points.Count + " points)";
            var sb = new System.Text.StringBuilder(Recon.Path(p.Fsm.transform) + "::" + p.Fsm.FsmName + " [" + p.Fsm.ActiveStateName + "]");
            foreach (FsmState st in p.Fsm.FsmStates) sb.Append(" ## ").Append(st.Name).Append(" : ").Append(Jobs.DumpActions(p.Fsm, st.Name));
            GameObject part = p.Fsm.FsmVariables.GetFsmGameObject("ActivePart").Value;
            PlayMakerFSM data = part != null ? Game.FsmOn(part, "Data") : null;
            if (data != null)
            {
                sb.Append(" ||| piece ").Append(part.name).Append(" local ").Append(part.transform.localPosition.ToString("F3")).Append(" ").Append(part.transform.localEulerAngles.ToString("F1")).Append(" parent ").Append(part.transform.parent != null ? part.transform.parent.name : "aucun");
                foreach (FsmState st in data.FsmStates) sb.Append(" ## ").Append(st.Name).Append(" : ").Append(Jobs.DumpActions(data, st.Name));
            }
            return sb.ToString();
        }

        // Essais : pieces a ID connues ici (nom, montee ou non), 80 au plus.
        public static string ListIds()
        {
            var sb = new System.Text.StringBuilder();
            int n = 0;
            foreach (Object o in Game.FsmsNamed("Data"))
            {
                var f = o as PlayMakerFSM;
                if (f == null || f.hideFlags != HideFlags.None) continue;
                FsmString s = f.FsmVariables.FindFsmString("ID");
                if (s == null || s.Value.Length == 0) continue;
                FsmBool inst = f.FsmVariables.FindFsmBool("Installed");
                sb.Append(" | ").Append(s.Value).Append(' ').Append(f.gameObject.name).Append(inst != null && inst.Value ? " [montee]" : "");
                if (++n >= 80) break;
            }
            return n + " pieces" + sb;
        }


        // ---------------------------------------------------------------- controle periodique (23/10, JD : « exactement comme l'hote »)
        // Jusqu'ici chaque montage, demontage et tour de vis etait envoye une fois : un message perdu, un rejeu rate (point pas encore
        // actif, automate occupe) ou une casse ignoree laissaient un ecart durable entre l'hote et un invite (siege monte chez l'un,
        // pas chez l'autre ; vis a 3 ici, 8 la-bas). L'hote envoie maintenant toutes les 10 s l'etat de ses points de montage
        // (empreinte de la cle, ID de la piece montee ou vide) et de ses vis (empreinte, cran), par paquets ; l'invite compare :
        //  - piece montee ici que l'hote n'a pas (ou une autre) : demontee ici (Remove part, comme un demontage recu) ;
        //  - piece montee chez l'hote, absente ici : demandee (@piecesdemande) ; l'hote renvoie le montage complet (Msg.Part, rejoue
        //    par OnMessage comme un montage normal, pose par rapport au point) ;
        //  - vis a un autre cran : TIGHTEN / UNTIGHTEN jusqu'au cran de l'hote (serrage de la piece recalcule).
        // Tout ce qui a bouge ici dans les 6 dernieres secondes (message en route, montage en cours) est laisse tranquille.
        // [Test] PiecesEcart=1 : chez l'invite, a 45 s, une piece montee est retiree sans rien envoyer ; le controle doit la ramener.
        static float nextDigest, lastDigestLog; static int digestLogs;
        static readonly Dictionary<uint, float> touched = new Dictionary<uint, float>();
        static Dictionary<uint, Point> pointsByHash; static int pointsByHashN = -1;
        static Dictionary<uint, Bolt> boltsByHash; static int boltsByHashN = -1;
        static bool ecartDone;
        static uint H(string s) { uint h = 2166136261; foreach (char ch in s) { h ^= ch; h *= 16777619; } return h; }
        static void Touch(uint h) { touched[h] = Time.realtimeSinceStartup; }
        static bool Recent(uint h, float now) { float t; return touched.TryGetValue(h, out t) && now - t < 6f; }
        static string BoltKeyOf(Bolt b) { return PartKey(b.F.FsmVariables.GetFsmGameObject("ThisPart").Value) + "|" + b.Key; }
        static void HashMaps()
        {
            if (pointsByHash == null || pointsByHashN != points.Count)
            {
                pointsByHash = new Dictionary<uint, Point>();
                foreach (KeyValuePair<string, Point> kv in points) pointsByHash[H(kv.Key)] = kv.Value;
                pointsByHashN = points.Count;
            }
            if (boltsByHash == null || boltsByHashN != bolts.Count)
            {
                boltsByHash = new Dictionary<uint, Bolt>();
                foreach (Bolt b in bolts) if (b.F != null) boltsByHash[H(BoltKeyOf(b))] = b;
                boltsByHashN = bolts.Count;
            }
        }
        static bool PendingReinstall(GameObject part) { foreach (Reinstall ri in reinstall) if (ri.Part == part) return true; return false; }
        // (une piece manquante introuvable ici serait redemandee a chaque controle : une fois par minute au plus, par point)
        static readonly Dictionary<uint, float> asked = new Dictionary<uint, float>();
        static bool AskedLately(uint h, float now) { float t; if (asked.TryGetValue(h, out t) && now - t < 60f) return true; asked[h] = now; return false; }

        static void Digest(float now)
        {
            if (!Session.Active || !Session.IsHost || Session.RemoteCount == 0 || now < nextDigest || now - loadedAt < 30f) return;
            nextDigest = now + 10f;
            if (Game.Msc) { MscDigest(now); return; }
            NetWriter w = null; int n = 0, total = 0, totalB = 0;
            foreach (KeyValuePair<string, Point> kv in points)
            {
                Point p = kv.Value;
                if (p.Fsm == null || !p.Fsm.gameObject.activeInHierarchy) continue;
                GameObject part = p.Fsm.FsmVariables.GetFsmGameObject("ActivePart").Value;
                string id = Holds(p.Fsm, part) ? IdOf(part) : "";
                if (w == null) { w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@pieces").U8(0); n = 0; }
                w.I32((int)H(p.Key)).Str(id); n++; total++;
                if (n >= 40) { Session.SendAll(w, true); w = null; }
            }
            if (w != null) Session.SendAll(w, true);
            w = null; n = 0;
            foreach (Bolt b in bolts)
            {
                if (b.F == null || !b.F.gameObject.activeInHierarchy) continue;
                FsmInt t = b.F.FsmVariables.FindFsmInt("BoltTightness"); if (t == null) continue;
                if (w == null) { w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@pieces").U8(1); n = 0; }
                w.I32((int)H(BoltKeyOf(b))).U8(Mathf.Clamp(t.Value, 0, 255)); n++; totalB++;
                if (n >= 100) { Session.SendAll(w, true); w = null; }
            }
            if (w != null) Session.SendAll(w, true);
            if (digestLogs++ < 3 || now >= lastDigestLog + 120f) { lastDigestLog = now; Log.Info("pieces : controle envoye aux invites (" + total + " points, " + totalB + " vis)"); }
        }

        public static void OnDigest(int who, NetReader r)
        {
            if (Session.IsHost) return;
            int kind = r.U8();
            float now = Time.realtimeSinceStartup;
            if (now - loadedAt < 30f || !PlayerSync.InGame) return;
            if (Game.Msc) { MscOnDigest(who, r, kind, now); return; }
            HashMaps();
            List<int> ask = null; int removed = 0, fixedBolts = 0, unknown = 0;
            if (kind == 0)
            {
                while (r.More)
                {
                    uint h = (uint)r.I32(); string id = r.Str();
                    Point p;
                    if (!pointsByHash.TryGetValue(h, out p) || p.Fsm == null) { unknown++; continue; }
                    if (Recent(h, now) || !p.Fsm.gameObject.activeInHierarchy) continue;
                    GameObject part = p.Fsm.FsmVariables.GetFsmGameObject("ActivePart").Value;
                    bool isIn = Holds(p.Fsm, part);
                    string mine = isIn ? IdOf(part) : "";
                    if (mine == id) continue;
                    if (isIn && (mounting.ContainsKey(part) || PendingReinstall(part))) continue;
                    if (isIn) { ReplayRemove(p, part, mine, who, "pas montee chez l'hote" + (id.Length > 0 ? " (il y a " + id + ")" : "")); removed++; }
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
                    uint h = (uint)r.I32(); int target = r.U8();
                    Bolt b;
                    if (!boltsByHash.TryGetValue(h, out b) || b.F == null) { unknown++; continue; }
                    if (Recent(h, now) || !b.F.gameObject.activeInHierarchy) continue;
                    FsmInt t = b.F.FsmVariables.FindFsmInt("BoltTightness");
                    if (t == null || t.Value == target) continue;
                    string part = PartKey(b.F.FsmVariables.GetFsmGameObject("ThisPart").Value);
                    if (ApplyBolt(who, part, b.Key, target, true)) { fixedBolts++; Touch(h); }
                }
            }
            int asked = ask != null ? ask.Count : 0;
            if (removed + fixedBolts + asked > 0)
                Log.Info("pieces : controle de l'hote : " + removed + " demontee(s) ici, " + asked + " demandee(s), " + fixedBolts + " vis reglee(s)" + (unknown > 0 ? ", " + unknown + " inconnue(s) ici" : ""));
        }

        // Hote : montages demandes par un invite (points ou il a un ecart), renvoyes comme des montages normaux.
        public static void OnDigestAsk(int who, NetReader r)
        {
            if (!Session.IsHost) return;
            if (Game.Msc) { MscOnDigestAsk(who, r); return; }
            HashMaps();
            int n = 0;
            while (r.More && n < 20)
            {
                uint h = (uint)r.I32(); n++;
                Point p;
                if (!pointsByHash.TryGetValue(h, out p) || p.Fsm == null) continue;
                GameObject part = p.Fsm.FsmVariables.GetFsmGameObject("ActivePart").Value;
                if (!Holds(p.Fsm, part)) continue;
                string id = IdOf(part); if (id.Length == 0) continue;
                PlayMakerFSM data = Game.FsmOn(part, "Data");
                FsmInt aid = data != null ? data.FsmVariables.FindFsmInt("AssemblyID") : null;
                FsmFloat tf = data != null ? data.FsmVariables.FindFsmFloat("Tightness") : null;
                Transform pt = p.Fsm.transform;
                Session.Broadcast(new NetWriter(Msg.Part).U8(Session.LocalId).Bool(true).Str(p.Key).Str(id).I32(aid != null ? aid.Value : 0)
                    .Vec(part.transform.position).Quat(part.transform.rotation)
                    .Vec(pt.InverseTransformPoint(part.transform.position)).Quat(Quaternion.Inverse(pt.rotation) * part.transform.rotation)
                    .F32(tf != null ? tf.Value : -1f).U8(0), true);
                Log.Info("pieces : montage de " + id + " sur " + p.Key + " renvoye a la demande de #" + who);
            }
        }

        static void ReplayRemove(Point p, GameObject part, string id, int who, string why)
        {
            VehicleSync.CalmAfterPart(p.Fsm.transform);
            applying = true; Replay.Depth++;
            try { Game.SetState(p.Fsm, "Remove part"); }
            finally { applying = false; Replay.Depth--; }
            Props.HoldFromRemote(part, who);
            Log.Info("piece " + id + " demontee de " + p.Key + " au controle de l'hote : " + why);
        }

        // [Test] PiecesEcart=1 (invite) : ecart provoque pour eprouver le controle.
        static void TestEcart(float now)
        {
            if (ecartDone || Session.IsHost || Config.GetInt("Test", "PiecesEcart", 0) == 0 || Time.timeSinceLevelLoad < 45f) return;
            ecartDone = true;
            foreach (KeyValuePair<string, Point> kv in points)
            {
                Point p = kv.Value;
                if (p.Fsm == null || !p.Fsm.gameObject.activeInHierarchy) continue;
                GameObject part = p.Fsm.FsmVariables.GetFsmGameObject("ActivePart").Value;
                if (!Holds(p.Fsm, part) || IdOf(part).Length == 0 || !p.Key.Contains("VINP_Seat")) continue;
                applying = true; Replay.Depth++;
                try { Game.SetState(p.Fsm, "Remove part"); }
                finally { applying = false; Replay.Depth--; }
                Log.Info("autotest : ecart provoque : " + IdOf(part) + " retiree de " + p.Key + " sans rien envoyer");
                break;
            }
            // (et une vis desserree sans rien envoyer : le controle doit la resserrer au cran de l'hote)
            foreach (Bolt b in bolts)
            {
                if (b.F == null || !b.F.gameObject.activeInHierarchy) continue;
                FsmInt t = b.F.FsmVariables.FindFsmInt("BoltTightness");
                if (t == null || t.Value < 2) continue;
                int was = t.Value;
                applying = true; Replay.Depth++;
                try { for (int g = 0; t.Value > 0 && g < 10; g++) b.F.SendEvent("UNTIGHTEN"); }
                finally { applying = false; Replay.Depth--; }
                Log.Info("autotest : ecart provoque : vis " + BoltKeyOf(b) + " " + was + " -> " + t.Value + " sans rien envoyer");
                break;
            }
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
            if (Game.Msc) { MscScan(); return; }
            int before = points.Count, beforeBolts = screws.Count;
            // (releve partage Game.AllFsms, frais de 0,5 s au plus -- avant : un releve complet de 70-90 ms a chaque point ou
            // piece inconnu, jusqu'a trois par message ; revue du 14/10)
            foreach (Object o in Game.AllFsms(0.5f))
            {
                var f = (PlayMakerFSM)o;
                if (f == null) continue;
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
                FsmHooks.Prepend(s, a);
                return true;
            }
            catch { return false; }
        }

        // Pieces en cours de montage (ici ou rejoue) : jusqu'a ce que le joint du jeu existe (etat Set joint 2, l'image
        // suivante), Props n'y touche pas et n'en envoie pas la pose (journaux et video de witefold, 14/10 : la main qui lache
        // la piece au montage envoyait encore sa pose « en train de se poser », l'autre cote la recopiait et le joint la
        // figeait de travers).
        static readonly Dictionary<GameObject, float> mounting = new Dictionary<GameObject, float>();
        public static bool IsMounting(GameObject go) { float t; return go != null && mounting.TryGetValue(go, out t) && Time.realtimeSinceStartup < t; }
        static readonly Dictionary<GameObject, Transform> mountingAt = new Dictionary<GameObject, Transform>();
        static readonly List<GameObject> mountedNow = new List<GameObject>();
        // Fin de la fenetre de montage : une ligne de journal (joint present ? pose par rapport au point de montage --
        // la meme chez tous quand tout va bien) pour les retours de pieces posees de travers.
        static void CheckMounting(float now)
        {
            foreach (KeyValuePair<GameObject, float> e in mounting) if (now >= e.Value) mountedNow.Add(e.Key);
            foreach (GameObject part in mountedNow)
            {
                mounting.Remove(part);
                Transform pt; mountingAt.TryGetValue(part, out pt); mountingAt.Remove(part);
                if (part == null || pt == null) continue;
                Rigidbody prb = part.GetComponent<Rigidbody>();
                Log.Info("piece " + IdOf(part) + " posee : joint " + (part.GetComponent<FixedJoint>() != null ? "oui" : "NON") + ", corps " + (prb == null ? "aucun" : prb.isKinematic ? "CINEMATIQUE" : "libre") + ", par rapport au point "
                    + pt.InverseTransformPoint(part.transform.position).ToString("F3") + " / " + (Quaternion.Inverse(pt.rotation) * part.transform.rotation).eulerAngles.ToString("F1"));
            }
            mountedNow.Clear();
        }

        // Casses (BREAKOFF) : une voiture dont la physique s'emballe chez un joueur (copie recalee, choc, NaN) fait passer tous
        // ses points en « Remove part » d'un coup ; chaque demontage partait comme un geste, l'hote rejouait et relayait tout :
        // voiture desossee chez tous (Rivett de Vaydon, 15/10). Une casse n'est envoyee que par celui qui fait autorite sur la
        // voiture, et 3 au plus par seconde et par voiture (8 en 2 s juste apres un vrai choc note par CarHits : rafale d'un
        // accident) ; l'hote refuse les autres (OnMessage). Ce qui n'est pas envoye n'est pas garde non plus : la piece est
        // remise ici (Reinstall), sinon celui qui a depasse le quota roulait seul avec une voiture desossee (16/10).
        // Points a attache incassable (InitialAttachForce = float.MaxValue : portieres, sieges, capot, coffre, ailes, pare-chocs,
        // calandre, batterie, reservoir... les 18 points a attache de la CORRIS, releve du 08/10) : le jeu n'y produit jamais de
        // BREAKOFF par la physique en solo -- un accident cabosse les toles sans rien arracher. Une telle casse ici, c'est une
        // attache rompue par une force infinie (copie recalee, NaN) : jamais envoyee, remise ici, meme avec l'autorite (retour
        // de deux joueurs, 16/10 : conducteur avec toutes ses pieces, l'autre sans portieres, sieges, capot ni coffre).
        class Burst { public float Since; public int N; }
        static readonly Dictionary<string, Burst> bursts = new Dictionary<string, Burst>();
        const int BurstCalm = 3, BurstCrash = 8;
        const float WindowCalm = 1f, WindowCrash = 2f;
        static bool TooMany(string carKey, float now, int limit, float window)
        {
            Burst b;
            if (!bursts.TryGetValue(carKey, out b)) bursts[carKey] = b = new Burst();
            if (now - b.Since > window) { b.Since = now; b.N = 0; }
            return ++b.N > limit;
        }

        static bool Unbreakable(Point p)
        {
            FsmFloat f = p != null && p.Fsm != null ? p.Fsm.FsmVariables.FindFsmFloat("InitialAttachForce") : null;
            return f != null && (f.Value >= 1e30f || float.IsInfinity(f.Value));
        }

        // Casse locale a ne pas garder (voir OnLocal) : la piece est remise sur son point 1 s plus tard, comme un montage rejoue.
        // Un etat recu entre-temps pour la piece (demontage ou montage par l'autorite) annule la remise (OnMessage).
        class Reinstall { public Point Point; public GameObject Part; public float At; }
        static readonly List<Reinstall> reinstall = new List<Reinstall>();
        static int reinstallLogs;
        static void QueueReinstall(Point p, GameObject part, string id, string why)
        {
            if (reinstallLogs++ < 40 || reinstallLogs % 50 == 0) Log.Warn("piece " + id + " cassee de " + p.Key + " : " + why + ", pas envoye, remise dans 1 s");
            foreach (Reinstall r in reinstall) if (r.Part == part) return;
            reinstall.Add(new Reinstall { Point = p, Part = part, At = Time.realtimeSinceStartup + 1f });
        }
        static void CheckReinstall(float now)
        {
            for (int i = reinstall.Count - 1; i >= 0; i--)
            {
                Reinstall r = reinstall[i];
                if (now < r.At) continue;
                reinstall.RemoveAt(i);
                if (r.Part == null || r.Point == null || r.Point.Fsm == null || Holds(r.Point.Fsm, r.Part)) continue;
                PlayMakerFSM data = Game.FsmOn(r.Part, "Data");
                if (data == null) continue;
                r.Point.Fsm.FsmVariables.GetFsmGameObject("ActivePart").Value = r.Part;
                applying = true; Replay.Depth++;
                try
                {
                    Transform pt = r.Point.Fsm.transform;
                    r.Part.transform.position = pt.position; r.Part.transform.rotation = pt.rotation;
                    Props.ReleaseRemote(r.Part);
                    mounting[r.Part] = now + 1f; mountingAt[r.Part] = pt;
                    data.SendEvent("INSTALL");
                    Game.SetState(r.Point.Fsm, "Install 2");
                }
                catch (System.Exception e) { Log.Warn("piece " + IdOf(r.Part) + " : remise impossible (" + e.Message + ")"); }
                finally { applying = false; Replay.Depth--; }
                if (reinstallLogs++ < 40 || reinstallLogs % 50 == 0) Log.Info("piece " + IdOf(r.Part) + " remise sur " + r.Point.Key + " (casse locale non gardee)");
            }
        }

        static void OnLocal(Point p, bool install) { OnLocal(p, install, false); }
        static void OnLocal(Point p, bool install, bool breakoff)
        {
            GameObject part = p.Fsm.FsmVariables.GetFsmGameObject("ActivePart").Value;
            if (install) Freshen(part, p.Fsm, IdOf(part));   // ses vis s'activent : accrochees avant le premier tour de cle
            if (!Session.Active || Time.realtimeSinceStartup - loadedAt < 20f) return;   // chargement : chacun le sien
            string id = IdOf(part);
            if (id.Length == 0) { Log.Warn("piece sans ID sur " + p.Key); return; }
            Touch(H(p.Key));
            int flags = breakoff ? 1 : 0;
            if (breakoff && Config.GetInt("Test", "CasseSansGarde", 0) == 0)   // ([Test] CasseSansGarde=1 : envoyee quand meme, pour eprouver le refus de l'hote)
            {
                Transform car = VehicleSync.CarRoot(p.Fsm.transform);
                Rigidbody cb = car != null ? car.GetComponent<Rigidbody>() : null;
                // (attache incassable dans le jeu : rompue ici par une force infinie, physique emballee -- jamais une vraie casse ;
                // les essais (TestBreak) passent outre, pour eprouver la rafale et le quota)
                if (!testBreak && Unbreakable(p)) { QueueReinstall(p, part, id, "attache incassable dans le jeu (force infinie), physique emballee ici"); return; }
                if (cb != null && VehicleSync.Authority(cb) != Session.LocalId)
                {
                    // (copie d'ici qui s'est emballee : la piece est toujours montee chez celui qui fait autorite -- remise ici dans 1 s)
                    QueueReinstall(p, part, id, "voiture sous l'autorite d'un autre");
                    return;
                }
                bool crash = CarHits.ImpactAgo(car) < WindowCrash;
                int limit = crash ? BurstCrash : BurstCalm;
                float window = crash ? WindowCrash : WindowCalm;
                if (TooMany(car != null ? car.name : p.Key, Time.realtimeSinceStartup, limit, window))
                { QueueReinstall(p, part, id, "plus de " + limit + " casses en " + window.ToString("F0") + " s" + (crash ? " malgre le choc" : "")); return; }
                if (crash) flags |= 2;   // (rafale d'accident : l'hote admet le meme quota -- OnMessage)
            }
            PlayMakerFSM data = Game.FsmOn(part, "Data");
            FsmInt aid = data != null ? data.FsmVariables.FindFsmInt("AssemblyID") : null;
            Log.Info((install ? "montage : " : breakoff ? "casse : " : "demontage : ") + id + " sur " + p.Key + ((flags & 2) != 0 ? " (choc il y a " + CarHits.ImpactAgo(VehicleSync.CarRoot(p.Fsm.transform)).ToString("F1") + " s)" : ""));
            VehicleSync.CalmAfterPart(p.Fsm.transform);   // (sursaut de la physique au montage : la voiture garee ne s'envole pas)
            // + pose dans le repere du point de montage : chez l'autre, la piece est posee la ou elle est par rapport a SA
            // voiture (posee a la pose du monde d'ici, elle entrait dans la carrosserie d'une copie a peine decalee, et la
            // voiture etait ejectee -- .nolly, 13/10 : siege conducteur pose par l'hote, CORRIS a 257 km/h chez l'invite)
            Transform pt = p.Fsm.transform;
            // + serrage de la piece (moteur : voir OnMessage)
            FsmFloat tf = data != null ? data.FsmVariables.FindFsmFloat("Tightness") : null;
            Session.SendAll(new NetWriter(Msg.Part).U8(Session.LocalId).Bool(install).Str(p.Key).Str(id).I32(aid != null ? aid.Value : 0)
                .Vec(part.transform.position).Quat(part.transform.rotation)
                .Vec(pt.InverseTransformPoint(part.transform.position)).Quat(Quaternion.Inverse(pt.rotation) * part.transform.rotation)
                .F32(tf != null ? tf.Value : -1f).U8(flags), true);
            if (install) { mounting[part] = Time.realtimeSinceStartup + 1f; mountingAt[part] = pt; }
            else Props.SettleHere(part);   // (ses poses de chute partent aux autres, comme un objet lache de la main)
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            if (Game.Msc) { MscOnMessage(from, r); return; }
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            bool install = r.Bool();
            string key = r.Str(), id = r.Str();
            int aid = r.I32();
            Vector3 pos = r.Vec();
            Quaternion rot = r.Quat();
            bool rel = r.More;
            Vector3 lpos = rel ? r.Vec() : Vector3.zero;
            Quaternion lrot = rel ? r.Quat() : Quaternion.identity;
            float tight = r.More ? r.F32() : -1f;
            int flags = r.More ? r.U8() : 0;
            bool breakoff = (flags & 1) != 0, crash = (flags & 2) != 0;
            Point p;
            if (!points.TryGetValue(key, out p) || p.Fsm == null) { Scan(); points.TryGetValue(key, out p); }
            Touch(H(key));
            if (Session.IsHost)
            {
                // Casse venue d'un invite : seulement s'il fait autorite sur la voiture, jamais d'un point a attache incassable
                // (physique emballee chez lui), et 3 par seconde au plus -- 8 en 2 s s'il annonce un choc (voir OnLocal).
                if (breakoff && p != null && p.Fsm != null)
                {
                    Transform car = VehicleSync.CarRoot(p.Fsm.transform);
                    Rigidbody cb = car != null ? car.GetComponent<Rigidbody>() : null;
                    if (cb != null && VehicleSync.Authority(cb) != who) { Log.Warn("piece " + id + " cassee de " + key + " par #" + who + " sans autorite sur la voiture : ignoree"); return; }
                    if (Unbreakable(p) && Config.GetInt("Test", "CasseSansGarde", 0) == 0)
                    { Log.Warn("piece " + id + " cassee de " + key + " par #" + who + " : attache incassable dans le jeu (physique emballee chez lui), ignoree"); return; }
                    int limit = crash ? BurstCrash : BurstCalm;
                    float window = crash ? WindowCrash : WindowCalm;
                    if (TooMany("#" + who + ":" + (car != null ? car.name : key), Time.realtimeSinceStartup, limit, window))
                    { Log.Warn("piece " + id + " cassee de " + key + " par #" + who + " : plus de " + limit + " casses en " + window.ToString("F0") + " s, ignoree"); return; }
                }
                var bw = new NetWriter(Msg.Part).U8(who).Bool(install).Str(key).Str(id).I32(aid).Vec(pos).Quat(rot);
                if (rel) bw.Vec(lpos).Quat(lrot);
                if (rel && tight >= 0f) bw.F32(tight).U8(flags);
                Session.Broadcast(bw, true, who);
            }
            if (rel && lpos.magnitude > 3f) { Log.Warn("piece " + id + " sur " + key + " : pose a " + lpos.magnitude.ToString("F1") + " m du point, ignoree"); return; }
            GameObject part = FindByIdCached(id);
            if (part == null) part = FindById(id);   // (achetee depuis le dernier releve des ID)
            if (p == null || part == null) { Log.Warn("piece " + id + " / point " + key + " introuvable ici"); return; }
            // L'etat recu de l'autorite prime sur une remise en attente ici (casse locale puis vraie casse ou demontage recus :
            // sans cela, la piece revenait ici 1 s plus tard et manquait chez lui).
            for (int i = reinstall.Count - 1; i >= 0; i--)
                if (reinstall[i].Part == part) { reinstall.RemoveAt(i); Log.Info("piece " + id + " : remise annulee, etat recu du joueur #" + who); }
            PlayMakerFSM data = Game.FsmOn(part, "Data");
            bool isIn = Holds(p.Fsm, part);
            if (install == isIn) { Log.Info("piece " + id + " deja " + (isIn ? "montee" : "demontee")); return; }
            p.Fsm.FsmVariables.GetFsmGameObject("ActivePart").Value = part;
            VehicleSync.CalmAfterPart(p.Fsm.transform);
            Trace.Watch(p.Fsm, "point " + key, 8f);
            Trace.Watch(data, "piece " + id, 8f);
            applying = true; Replay.Depth++;
            try
            {
                if (install)
                {
                    FsmInt a = data.FsmVariables.FindFsmInt("AssemblyID");
                    if (a != null) a.Value = aid;
                    // La piece exactement ou le joueur l'a posee (le jeu la fixe la ou elle est) -- par rapport au point de
                    // montage d'ici quand l'envoi le donne (voiture d'ici un peu ailleurs que celle de l'autre).
                    if (rel) { Transform pt = p.Fsm.transform; part.transform.position = pt.TransformPoint(lpos); part.transform.rotation = pt.rotation * lrot; }
                    else { part.transform.position = pos; part.transform.rotation = rot; }
                    // Moteur (VINP_Block) : le jeu le pose sur son point quand ses vis le tiennent (Tightness >= 1, etat Bolted),
                    // pendu au palan. Ici ses vis n'ont pas forcement suivi (actives seulement a 5 cm de leur detecteur) :
                    // avec Tightness 0, INSTALL le ferait retirer aussitot (Tightness? -> Unbolted -> Remove). On lui donne
                    // le serrage de l'envoyeur. Et s'il pend a un palan ici (joint fixe, crochet rejoue par Garage), le joint
                    // saute avant que le jeu retire son corps -- comme l'etat State 2 du crochet.
                    FsmFloat tf = tight >= 0f ? data.FsmVariables.FindFsmFloat("Tightness") : null;
                    if (tf != null && tf.Value < tight) tf.Value = tight;
                    FixedJoint fj = part.GetComponent<FixedJoint>();
                    if (fj != null) Object.Destroy(fj);
                    Props.ReleaseRemote(part);   // (tenue par lui ici : plus cinematique avant le joint du jeu)
                    mounting[part] = Time.realtimeSinceStartup + 1f; mountingAt[part] = p.Fsm.transform;
                    data.SendEvent("INSTALL");
                    Game.SetState(p.Fsm, "Install 2");
                }
                else Game.SetState(p.Fsm, "Remove part");
            }
            finally { applying = false; Replay.Depth--; }
            if (install) Freshen(part, p.Fsm, id);   // ses vis, avant les tours de cle de l'autre
            else Props.HoldFromRemote(part, who);   // (tenue le temps que ses poses de chute arrivent : pas ejectee par la physique d'ici)
            Log.Info("piece " + id + (install ? " montee sur " : breakoff ? " cassee de " : " demontee de ") + key + " (joueur #" + who + ")" + (install ? " [parent " + (part.transform.parent != null ? part.transform.parent.name : "aucun") + ", local " + part.transform.localPosition.ToString("F2") + "]" : ""));
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
            foreach (Object o in Game.AllFsms(0.5f))
            {
                var f = (PlayMakerFSM)o;
                if (f == null) continue;
                if (f.hideFlags != HideFlags.None || f.FsmName != "Data") continue;
                FsmString s = f.FsmVariables.FindFsmString("ID");
                if (s != null && s.Value.Length > 0) idMap[s.Value] = f.gameObject;
            }
            return idMap.TryGetValue(id, out go) ? go : null;
        }

        public static GameObject FindById(string id)
        {
            foreach (Object o in Game.AllFsms(0.5f))
            {
                var f = (PlayMakerFSM)o;
                if (f == null) continue;
                if (f.hideFlags != HideFlags.None || f.FsmName != "Data") continue;
                FsmString s = f.FsmVariables.FindFsmString("ID");
                if (s != null && s.Value == id) return f.gameObject;
            }
            return null;
        }

        // Essais : demonte la piece 'id' comme une casse (BREAKOFF) -- le crochet la traite comme telle.
        static bool testBreak;
        public static string TestBreak(string id)
        {
            testBreak = true;
            try { return TestToggle(id); } finally { testBreak = false; }
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
        // l'hote monte la piece a 30 s au plus tot (l'invite en partie depuis 15 s), puis serre tout de suite sa
        // premiere vis de 3 crans (0,5 s, 1 s, 1,5 s apres le montage : avant tout releve periodique) ; a 45 et
        // 60 s chacun note les vis de la piece. Attendu chez l'invite : "piece ... montee", trois "vis
        // <ID>/Bolts/BoltPM#k/boltk = 1, 2, 3 (joueur #0)" et le meme etat qu'a l'hote ("...=3"), aucune
        // "vis ... introuvable".
        static int testStep, testLogs;
        static float testAt;

        // [Test] Autotest=casse2 (2 joueurs, CORRIS assemblee -- sauvegarde Rivett) : a 25 s, l'HOTE « casse » en rafale (BREAKOFF,
        // comme un accident) les pieces montees sur les points de [Test] TestPoints (portieres, sieges, capot, coffre de la CORRIS
        // par defaut), apres avoir note un choc sur la voiture ([Test] CasseChoc=1, defaut : rafale admise, 8 en 2 s) ; a 45 s,
        // chacun note l'etat des points : "autotest casse2 : VINP_DoorLeft=vide ; ...". Attendu : la meme liste chez les deux,
        // tout vide. Avec CasseChoc=0 : quota de 3 par seconde, les 3 autres sont remises chez l'hote ("plus de 3 casses en 1 s,
        // pas envoye, remise dans 1 s") -- les memes 3 points vides chez les deux.
        static int test2Step, test2Logs;
        const string TestPointsDefault = "CORRIS/Assemblies/VINP_DoorLeft,CORRIS/Assemblies/VINP_DoorRight,CORRIS/Assemblies/VINP_SeatDriver,"
                                       + "CORRIS/Assemblies/VINP_SeatPassenger,CORRIS/Assemblies/VINP_Hood,CORRIS/Assemblies/VINP_Bootlid";

        static void TestCasse2(float t)
        {
            string[] keys = Config.Get("Test", "TestPoints", TestPointsDefault).Split(',');
            if (Session.IsHost && t > 25f && test2Step == 0 && Jobs.OtherInGame(15f))
            {
                test2Step = 1;
                if (Config.GetInt("Test", "CasseChoc", 1) != 0)
                {
                    Point p0;
                    Transform car = points.TryGetValue(keys[0].Trim(), out p0) && p0.Fsm != null ? VehicleSync.CarRoot(p0.Fsm.transform) : null;
                    CarHits.NoteImpact(car);
                    Log.Info("autotest casse2 : choc note sur " + (car != null ? car.name : "? (point " + keys[0].Trim() + " inconnu)"));
                }
                foreach (string k in keys) Log.Info("autotest casse2 : casse " + k.Trim() + " : " + TestBreakPoint(k.Trim()));
            }
            if (test2Logs == 0 && t > 45f) { test2Logs = 1; Log.Info("autotest casse2 : " + PointsState(keys) + " (" + reinstall.Count + " remises en attente)"); }
        }

        // Essais : demonte comme une casse (BREAKOFF) la piece montee sur le point de cle 'key'.
        public static string TestBreakPoint(string key)
        {
            Point p;
            if (!points.TryGetValue(key, out p) || p.Fsm == null) { Scan(); if (!points.TryGetValue(key, out p) || p.Fsm == null) return "point inconnu (" + points.Count + " points)"; }
            GameObject part = p.Fsm.FsmVariables.GetFsmGameObject("ActivePart").Value;
            if (part == null || !Holds(p.Fsm, part)) return "rien de monte";
            testBreak = true;
            try { Game.SetState(p.Fsm, "Remove part"); } finally { testBreak = false; }
            return "casse de " + IdOf(part);
        }

        // Essais : pour chaque point (cle), l'ID de la piece montee ou "vide".
        public static string PointsState(string[] keys)
        {
            var sb = new System.Text.StringBuilder();
            foreach (string k0 in keys)
            {
                string k = k0.Trim();
                Point p;
                sb.Append(sb.Length > 0 ? " ; " : "").Append(k.Substring(k.LastIndexOf('/') + 1)).Append('=');
                if (!points.TryGetValue(k, out p) || p.Fsm == null) { sb.Append('?'); continue; }
                GameObject part = p.Fsm.FsmVariables.GetFsmGameObject("ActivePart").Value;
                sb.Append(part != null && Holds(p.Fsm, part) ? IdOf(part) : "vide");
            }
            return sb.ToString();
        }

        public static void Test(string mode, float t)
        {
            if (mode == "casse2") { TestCasse2(t); return; }
            if (mode != "boulons") return;
            string id = Config.Get("Test", "TestPiece", "VIN413C1");
            float now = Time.realtimeSinceStartup;
            if (Session.IsHost && t > 30f && testStep == 0 && Jobs.OtherInGame(15f)) { testStep = 1; testAt = now; Log.Info("autotest : boulons, " + TestToggle(id)); }
            if (Session.IsHost && testStep >= 1 && testStep <= 3 && now - testAt > 0.5f * testStep)
            { testStep++; Log.Info("autotest : boulons, " + (now - testAt).ToString("F1") + " s apres le montage, vis " + TestBolt(id, true)); }
            if (testLogs < 2 && t > 45f + 15f * testLogs)
            {
                testLogs++;
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
