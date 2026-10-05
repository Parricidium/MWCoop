using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Boites qui donnent un objet a chaque prise : fusibles (fusepackage), bougies (sparkplugbox), ampoule
    // (lightbulbbox), piles (r20batterybox), boites de pieces neuves (boxalternator0, boxpistons0...), ressorts
    // (SpringsBox). Leur automate Use (ID pose par le jeu) passe au clic dans "Create Fuse" / "Create Plug" :
    // Quantity - 1, SetFsmGameObject/SetGameObject (point d'apparition), puis SendEventByName SPAWNITEM a un
    // distributeur (Spawner/CreateItems :: Fuse, Sparkplug, Lightbulb, R20Battery ; CARPARTS/.../SPAWNERS_* pour les
    // pieces) dont l'etat "Create product" cree l'objet et lui donne son nom -- donc son ID -- par son compteur
    // (ObjectNumberInt + 1, sauvegarde). Avant, seul celui qui ouvrait la boite avait l'objet, et les comptes des
    // boites divergeaient. WorldFsms ne rejoue pas ces automates (objets "(itemx)").
    // Chez celui qui prend : un crochet en tete de l'etat ouvre la prise ; l'observateur des createurs (Consume :
    // PreSpawn en tete, Spawned en fin de chaque etat qui cree) note pendant l'etat chaque objet cree (distributeur,
    // compteurs AVANT la creation, pose) ; une action en fin d'etat envoie le tout avec le compte restant.
    // Chez les autres : l'etat est rejoue (meme chaine du jeu, meme distributeur, meme son) ; juste avant la creation,
    // les compteurs du distributeur en retard sont remis a ceux de l'autre (l'objet recoit le meme nom, donc le meme
    // ID), puis l'objet est pose ou l'autre l'a eu et le compte de la boite recopie. Props suit ensuite sa physique
    // par cet ID (un seul objet par prise chez chacun). La fin "Empty" de la boite videe part par Consume.
    // Compteur deja PLUS LOIN ici (deux prises en meme temps) : rien n'est force (jamais un nom deja donne ici),
    // l'objet recoit le numero suivant -- note au journal.
    // Ressorts (SpringsBox) : l'ouverture lance une boucle avec attentes qui sort les ressorts un par un ; rejouee
    // telle quelle, elle les sort chez chacun avec les memes compteurs (comme un sac de courses).
    public static class Boxes
    {
        // Objet cree pendant une prise : distributeur (nom de l'automate), compteurs qui ont change (valeur AVANT la
        // creation), pose de l'objet cree.
        public class Spawn
        {
            public string Fsm;
            public List<KeyValuePair<string, int>> Before = new List<KeyValuePair<string, int>>();
            public Vector3 Pos; public Quaternion Rot = Quaternion.identity;
        }

        class Taking { public string Id, State; public PlayMakerFSM F; public List<Spawn> Spawns = new List<Spawn>(); }
        static Taking taking;            // prise ici en cours (entre le crochet de tete et l'action de fin)
        static Spawn current;            // creation en cours pendant cette prise
        static List<Spawn> replaying;    // rejeu : creations attendues, dans l'ordre
        static Spawn replayCur;
        static readonly HashSet<FsmState> warned = new HashSet<FsmState>();
        // Objets crees par une prise (ici ou rejouee) : pour les essais.
        static readonly List<KeyValuePair<GameObject, string>> made = new List<KeyValuePair<GameObject, string>>();

        public static bool Busy { get { return taking != null || replaying != null; } }

        public static void OnLevelLoaded()
        {
            taking = null; current = null; replaying = null; replayCur = null;
            warned.Clear(); made.Clear();
            testStep = 0; testLog = 0; testBox = null; testBags = null;
        }

        // Etat de prise d'une boite : "Create Fuse", "Create Plug" (automate Use a ID).
        public static bool IsCreate(FsmState s) { return s != null && s.Name.StartsWith("Create ") && s.Name != "Create product"; }

        public static bool IsBox(PlayMakerFSM f)
        {
            foreach (FsmState s in f.Fsm.States) if (IsCreate(s)) return true;
            return false;
        }

        class Start : ModHook
        {
            public override string Module { get { return "consommables"; } }
            public PlayMakerFSM F; public string Id;
            public override void OnEnter()
            {
                try
                {
                    if (!Consume.Applying && Replay.Depth == 0) { taking = new Taking { Id = Id, State = State.Name, F = F }; current = null; }
                }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        // En fin d'etat, apres le SendEventByName (le distributeur a deja cree l'objet) : envoi de la prise.
        class End : FsmStateAction
        {
            public PlayMakerFSM F;
            public override void OnEnter()
            {
                try
                {
                    if (taking != null && taking.F == F && taking.State == State.Name) { Taking t = taking; taking = null; current = null; Send(t); }
                }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        // Consume.TryHook : crochets des etats de prise. Nombre d'etats accroches.
        public static int Hook(PlayMakerFSM f, string id)
        {
            int n = 0;
            foreach (FsmState s in f.Fsm.States)
            {
                if (!IsCreate(s)) continue;
                try
                {
                    var list = new List<FsmStateAction>(s.Actions);
                    list.Insert(0, new Start { F = f, Id = id });
                    list.Add(new End { F = f });
                    s.Actions = list.ToArray();
                    n++;
                    if (Consume.TouchesPlayer(s) && warned.Add(s)) Log.Warn("boites : " + id + " '" + s.Name + "' touche au joueur (objet pose ensuite a la place recue)");
                }
                catch { }
            }
            return n;
        }

        static List<KeyValuePair<string, int>> Ints(PlayMakerFSM sp)
        {
            var r = new List<KeyValuePair<string, int>>();
            foreach (FsmInt v in sp.FsmVariables.IntVariables) r.Add(new KeyValuePair<string, int>(v.Name, v.Value));
            return r;
        }

        // Consume.PreSpawn : un distributeur va creer un objet pendant une prise (ici : compteurs notes ; rejeu :
        // compteurs en retard remis a ceux de l'autre).
        public static void BeforeCreate(PlayMakerFSM sp)
        {
            if (sp == null) return;
            if (taking != null) { current = new Spawn { Fsm = sp.FsmName, Before = Ints(sp) }; return; }
            if (replaying == null) return;
            Spawn e = replaying.Find(x => x.Fsm == sp.FsmName);
            if (e == null) return;
            replaying.Remove(e);
            replayCur = e;
            foreach (KeyValuePair<string, int> kv in e.Before)
            {
                if (!Game.LocalVar(sp, kv.Key)) continue;
                FsmInt v = sp.FsmVariables.FindFsmInt(kv.Key);
                if (v == null || v.Value == kv.Value) continue;
                if (v.Value < kv.Value) { Log.Info("boites : " + sp.gameObject.name + "::" + sp.FsmName + " " + kv.Key + " " + v.Value + " -> " + kv.Value + " (rattrape celui de l'autre)"); v.Value = kv.Value; }
                else Log.Warn("boites : " + sp.gameObject.name + "::" + sp.FsmName + " " + kv.Key + " deja a " + v.Value + " ici (" + kv.Value + " chez l'autre : prises en meme temps), numero suivant");
            }
        }

        // Consume.Spawned : objet cree par le distributeur pendant la prise.
        public static void Created(PlayMakerFSM sp, GameObject go)
        {
            if (sp == null || go == null) return;
            if (taking != null && current != null && current.Fsm == sp.FsmName)
            {
                var changed = new List<KeyValuePair<string, int>>();
                foreach (KeyValuePair<string, int> kv in current.Before)
                {
                    FsmInt v = sp.FsmVariables.FindFsmInt(kv.Key);
                    if (v != null && v.Value != kv.Value && changed.Count < 3) changed.Add(kv);
                }
                current.Before = changed;
                current.Pos = go.transform.position; current.Rot = go.transform.rotation;
                if (taking.Spawns.Count < 4) taking.Spawns.Add(current);
                current = null;
                Note(go, "pris ici");
                return;
            }
            if (replaying == null || replayCur == null) return;
            Spawn e = replayCur;
            replayCur = null;
            go.transform.position = e.Pos; go.transform.rotation = e.Rot;
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb != null) { rb.position = e.Pos; rb.rotation = e.Rot; if (!rb.isKinematic) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; } }
            Note(go, "rejoue");
        }

        static void Note(GameObject go, string how)
        {
            if (made.Count >= 32) made.RemoveAt(0);
            made.Add(new KeyValuePair<GameObject, string>(go, how));
        }

        static void Send(Taking t)
        {
            List<KeyValuePair<string, object>> vars = Consume.ContentValues(t.F);
            var names = new System.Text.StringBuilder();
            foreach (Spawn s in t.Spawns) names.Append(s.Fsm).Append(' ');
            Log.Info("boites : " + t.Id + " '" + t.State + "' ici : " + t.Spawns.Count + " objet(s) cree(s) (" + names.ToString().TrimEnd() + "), reste " + Consume.Describe(vars));
            Consume.SendBox(t.Id, t.State, vars, t.Spawns);
        }

        // Prise recue (Consume.Dispatch, rejeu en cours : Applying et Replay.Depth deja poses).
        public static void Apply(PlayMakerFSM f, string id, string state, int who, List<KeyValuePair<string, object>> vars, List<Spawn> spawns)
        {
            FsmState s = f.Fsm.GetState(state);
            if (s == null || !IsCreate(s)) { Log.Warn("boites : " + id + " sans etat '" + state + "' ici"); return; }
            if (Consume.IsGone(f) && f.ActiveStateName != "Empty") { Log.Info("boites : " + id + " deja fini ici (" + f.ActiveStateName + "), prise du joueur #" + who + " ignoree"); return; }
            int before = made.Count;
            replaying = new List<Spawn>(spawns);
            replayCur = null;
            int left;
            try { Game.SetState(f, state); }
            finally { left = replaying.Count; replaying = null; replayCur = null; }
            Consume.SetValues(f, vars);
            var names = new System.Text.StringBuilder();
            for (int i = before; i < made.Count; i++) if (made[i].Key != null) names.Append(made[i].Key.name).Append(' ');
            Log.Info("boites : " + id + " '" + state + "' du joueur #" + who + " rejoue : " + (made.Count - before) + " objet(s) (" + names.ToString().TrimEnd() + ")"
                     + (left > 0 ? ", " + left + " attendu(s) jamais crees ici" : "") + ", reste " + Consume.Describe(vars) + " -> " + f.ActiveStateName);
        }

        public static void Write(NetWriter w, List<Spawn> l)
        {
            int n = Mathf.Min(l.Count, 4);   // message < 1100 octets
            w.U8(n);
            for (int i = 0; i < n; i++)
            {
                Spawn s = l[i];
                int c = Mathf.Min(s.Before.Count, 3);
                w.Str(s.Fsm).U8(c);
                for (int k = 0; k < c; k++) w.Str(s.Before[k].Key).I32(s.Before[k].Value);
                w.Vec(s.Pos).Quat(s.Rot);
            }
        }

        public static List<Spawn> Read(NetReader r)
        {
            int n = r.U8();
            var l = new List<Spawn>(n);
            for (int i = 0; i < n; i++)
            {
                var s = new Spawn { Fsm = r.Str() };
                int c = r.U8();
                for (int k = 0; k < c; k++) { string name = r.Str(); s.Before.Add(new KeyValuePair<string, int>(name, r.I32())); }
                s.Pos = r.Vec(); s.Rot = r.Quat();
                l.Add(s);
            }
            return l;
        }

        // ---------------------------------------------------------------- essais
        // [Test] Autotest=boite (TestPos dans le magasin) : a 30 s l'hote cherche la boite suivie la plus proche pas
        // encore vide ([Test] TestPiece : debut d'ID, fusepackage, sparkplugbox... ; aucune : achat de [Test]
        // TestAchat, FusePackage par defaut, sac vide 3 s plus tard, boite cherchee 7 s apres l'achat) et y prend un
        // objet comme le clic (etat "Create ..."), puis un 2e 6 s plus tard. Chacun note toutes les 2 s de 28 a 70 s
        // les boites a moins de 80 m (Quantity, etat) et les objets crees par une prise (nom, ID, place). Attendu :
        // chez l'invite « boites : fusepackageN 'Create Fuse' du joueur #0 rejoue : 1 objet(s) (fuseK) », la meme
        // Quantity et les memes ID aux memes places des deux cotes (un seul objet par prise).
        static int testStep;
        static float testAt, testLog;
        static PlayMakerFSM testBox;
        static HashSet<string> testBags;

        public static void Test(string mode, float t)
        {
            if (mode != "boite") return;
            string prefix = Config.Get("Test", "TestPiece", "");
            if (Session.IsHost && t > 30f && testStep == 0)
            {
                testStep = 1;
                testAt = t;
                testBox = FindBox(prefix);
                if (testBox == null)
                {
                    string product = Config.Get("Test", "TestAchat", "FusePackage");
                    if (product.Length > 0) { testBags = Consume.TestBags(); Log.Info("autotest : boite, aucune ici : " + Shop.TestBuy(product, 1)); }
                }
            }
            if (Session.IsHost && testStep == 1 && testBox == null && testBags != null && t > testAt + 3f) { Log.Info("autotest : " + Consume.TestOpenBag(testBags)); testBags = null; }
            if (Session.IsHost && testStep == 1 && (testBox != null || t > testAt + 7f))
            {
                if (testBox == null) testBox = FindBox(prefix);
                testStep = testBox != null ? 2 : 9;
                testAt = t;
                if (testBox == null) Log.Info("autotest : boite, aucune boite a ouvrir");
                else Take(testBox);
            }
            if (Session.IsHost && testStep == 2 && t > testAt + 6f) { testStep = 3; if (testBox != null) Take(testBox); }
            if (t > 28f && t < 70f && t - testLog >= 2f)
            {
                testLog = t;
                Log.Info("autotest : boites " + List(80f));
                var sb = new System.Text.StringBuilder();
                foreach (KeyValuePair<GameObject, string> kv in made)
                {
                    if (kv.Key == null) { sb.Append("(disparu) ; "); continue; }
                    sb.Append(kv.Key.name).Append(" [").Append(Props.ItemId(kv.Key)).Append("] ").Append(kv.Key.transform.position.ToString("F2")).Append(' ').Append(kv.Value).Append(" ; ");
                }
                Log.Info("autotest : boites, crees " + (sb.Length > 0 ? sb.ToString() : "aucun"));
            }
        }

        static void Take(PlayMakerFSM f)
        {
            FsmState c = null;
            foreach (FsmState s in f.Fsm.States) if (IsCreate(s)) { c = s; break; }
            if (c == null) return;
            string id = f.FsmVariables.FindFsmString("ID").Value;
            Game.SetState(f, c.Name);
            FsmInt q = f.FsmVariables.FindFsmInt("Quantity");
            Log.Info("autotest : boite " + id + " '" + c.Name + "' -> " + f.ActiveStateName + " (Quantity=" + (q != null ? q.Value.ToString() : "?") + ")");
        }

        // Boite suivie la plus proche, pas vide (Quantity > 0 s'il y en a une, pas dans une fin).
        static PlayMakerFSM FindBox(string prefix)
        {
            Consume.ScanNow();
            GameObject pl = GameObject.Find("PLAYER");
            Vector3 me = pl != null ? pl.transform.position : Vector3.zero;
            PlayMakerFSM best = null;
            foreach (KeyValuePair<string, PlayMakerFSM> kv in Consume.ById)
            {
                PlayMakerFSM f = kv.Value;
                if (f == null || !kv.Key.StartsWith(prefix) || !IsBox(f) || Consume.IsGone(f)) continue;
                FsmInt q = Game.LocalVar(f, "Quantity") ? f.FsmVariables.FindFsmInt("Quantity") : null;
                if (q != null && q.Value <= 0) continue;
                if (best == null || (f.transform.position - me).sqrMagnitude < (best.transform.position - me).sqrMagnitude) best = f;
            }
            return best;
        }

        static string List(float radius)
        {
            GameObject pl = GameObject.Find("PLAYER");
            Vector3 me = pl != null ? pl.transform.position : Vector3.zero;
            var keys = new List<string>();
            foreach (KeyValuePair<string, PlayMakerFSM> kv in Consume.ById)
                if (kv.Value != null && IsBox(kv.Value) && (kv.Value.transform.position - me).sqrMagnitude < radius * radius) keys.Add(kv.Key);
            keys.Sort(System.StringComparer.Ordinal);
            var sb = new System.Text.StringBuilder();
            foreach (string k in keys)
            {
                PlayMakerFSM f = Consume.ById[k];
                FsmInt q = f.FsmVariables.FindFsmInt("Quantity");
                sb.Append(k).Append(" [").Append(f.ActiveStateName).Append("] Quantity=").Append(q != null ? q.Value.ToString() : "-").Append(" ; ");
            }
            return sb.Length > 0 ? sb.ToString() : "aucune";
        }
    }
}
