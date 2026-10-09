using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Client du taxi (JOBS/TAXIJOB/Customer1/TaxiWalker) : son apparence est tiree au sort chez chacun -- 'Material' de Char
    // (visage, haut, pantalon : listes face / shirt / pants ; corpulence : maillage BodymeshFat / Normal / Thin) et
    // 'Randomize' de Accessories (casquette ou chapeau, lunettes, cigarette : copies d'objets modeles). Chaque joueur voyait
    // un autre client (retour d'un joueur, 09/10 : « le passager etait different pour mon ami »). L'hote fait foi : il envoie
    // ce qu'il voit (@client) quand ca change et toutes les 10 s (invite arrive entre-temps) ; chez les autres c'est
    // applique, et reapplique si leur propre tirage repasse dessus. A l'arrivee d'un invite, l'etat du client (Logic,
    // bagages : Jobs) lui est aussi envoye.
    public static class TaxiCustomer
    {
        const string WalkerPath = "JOBS/TAXIJOB/Customer1/TaxiWalker";
        static GameObject walker, ch, rag;
        static PlayMakerFSM mat, acc;
        static SkinnedMeshRenderer body, ragBody;
        static Transform accRoot;
        static string sent = "", wanted;
        static float nextLook, nextSend, nextFind;
        static readonly List<KeyValuePair<float, Peer>> snapshots = new List<KeyValuePair<float, Peer>>();
        static readonly string[] AccSlots = { "Hats", "HatsReverse", "Eyewear", "Cig" };

        public static void OnLevelLoaded() { walker = null; ch = null; mat = null; acc = null; body = null; ragBody = null; accRoot = null; sent = ""; wanted = null; nextFind = 0; snapshots.Clear(); }

        public static void ScheduleSnapshot(Peer p) { if (Session.IsHost) snapshots.Add(new KeyValuePair<float, Peer>(Time.realtimeSinceStartup + 20f, p)); }

        static bool Find()
        {
            if (body != null && mat != null) return true;
            if (Time.realtimeSinceStartup < nextFind) return false;
            nextFind = Time.realtimeSinceStartup + 5f;
            walker = Game.FindAny(WalkerPath);
            if (walker == null) return false;
            Transform c = walker.transform.Find("Char"), r = walker.transform.Find("RagDoll");
            if (c == null) return false;
            ch = c.gameObject;
            mat = Game.FsmOn(ch, "Material");
            Transform bm = c.Find("bodymesh"), rbm = r != null ? r.Find("bodymesh") : null;
            body = bm != null ? bm.GetComponent<SkinnedMeshRenderer>() : null;
            ragBody = rbm != null ? rbm.GetComponent<SkinnedMeshRenderer>() : null;
            foreach (Transform t in c.GetComponentsInChildren<Transform>(true)) if (t.name == "Accessories") { accRoot = t; break; }
            acc = accRoot != null ? Game.FsmOn(accRoot.gameObject, "Randomize") : null;
            if (body != null) Log.Info("taxi : apparence du client suivie (" + (acc != null ? "accessoires compris" : "sans accessoires") + ")");
            return body != null && mat != null;
        }

        static string BaseName(string n) { return n.Replace("(Clone)", "").Replace(" (Instance)", "").Trim(); }

        // Ce que montre le client ici : matieres 0-2, corpulence, accessoires par emplacement.
        static string Look()
        {
            var sb = new System.Text.StringBuilder();
            Material[] ms = body.sharedMaterials;
            for (int i = 0; i < 3; i++) sb.Append(i < ms.Length && ms[i] != null ? BaseName(ms[i].name) : "").Append('|');
            sb.Append(BodyKind()).Append('|');
            foreach (string slot in AccSlots)
            {
                Transform s = accRoot != null ? accRoot.Find(slot) : null;
                string n = "";
                if (s != null) foreach (Transform x in s) if (x.gameObject.activeSelf) { n = BaseName(x.name); break; }
                sb.Append(n).Append('|');
            }
            return sb.ToString();
        }

        static string BodyKind()
        {
            Mesh m = body.sharedMesh;
            foreach (string k in new[] { "BodymeshFat", "BodymeshNormal", "BodymeshThin" })
            {
                FsmObject o = mat.FsmVariables.FindFsmObject(k);
                if (o != null && o.Value == m) return k;
            }
            return "";
        }

        public static void Update()
        {
            float now = Time.realtimeSinceStartup;
            for (int i = snapshots.Count - 1; i >= 0; i--)
            {
                if (now < snapshots[i].Key) continue;
                Peer p = snapshots[i].Value; snapshots.RemoveAt(i);
                if (!p.Accepted) continue;
                int n = Jobs.SnapshotTo(p, "TAXIJOB/Customer1");
                sent = "";   // (apparence renvoyee aussi)
                Log.Info("taxi : etat du client (" + n + " automates) envoye a " + p);
            }
            if (now < nextLook || !Find()) return;
            nextLook = now + 1f;
            if (ch == null || !ch.activeInHierarchy) return;
            string look = Look();
            if (Session.IsHost)
            {
                if (look == sent && now < nextSend) return;
                if (look != sent) Log.Info("taxi : client " + look + " (envoye)");
                sent = look; nextSend = now + 10f;
                Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@client").Str(look), true);
                return;
            }
            if (wanted != null && look != wanted) Apply(wanted, look);
        }

        public static void OnRemote(int who, string look)
        {
            if (Session.IsHost) return;
            wanted = look;
            if (!Find() || ch == null || !ch.activeInHierarchy) return;
            string mine = Look();
            if (mine != look) Apply(look, mine);
        }

        static Material FindMat(string name)
        {
            if (name.Length == 0) return null;
            foreach (Material m in Resources.FindObjectsOfTypeAll(typeof(Material))) if (m.name == name) return m;
            return null;
        }

        static GameObject Model(string name)
        {
            if (name.Length == 0 || acc == null) return null;
            foreach (FsmState st in acc.Fsm.States)
                foreach (FsmStateAction a in st.Actions)
                {
                    if (a == null) continue;
                    foreach (System.Reflection.FieldInfo fi in a.GetType().GetFields())
                    {
                        object v = fi.GetValue(a);
                        var arr = v as FsmGameObject[];
                        if (arr != null) foreach (FsmGameObject g in arr) if (g != null && g.Value != null && g.Value.name == name) return g.Value;
                        var fg = v as FsmGameObject;
                        if (fg != null && fg.Value != null && fg.Value.name == name && fg.Value.transform.root != walker.transform.root) return fg.Value;
                    }
                }
            return null;
        }

        static string lastTry;
        static void Apply(string look, string mine)
        {
            if (lastTry == look + "#" + mine) return;   // (deja essaye tel quel : pas de reprise a chaque seconde)
            lastTry = look + "#" + mine;
            string[] w = look.Split('|');
            if (w.Length < 8) return;
            var changes = new List<string>();
            Material[] ms = body.sharedMaterials;
            bool matChanged = false;
            for (int i = 0; i < 3 && i < ms.Length; i++)
            {
                if (ms[i] != null && BaseName(ms[i].name) == w[i]) continue;
                Material m = FindMat(w[i]);
                if (m == null) continue;
                ms[i] = m; matChanged = true; changes.Add(w[i]);
            }
            if (matChanged)
            {
                body.sharedMaterials = ms;
                if (ragBody != null) { Material[] rs = ragBody.sharedMaterials; for (int i = 0; i < 3 && i < rs.Length; i++) rs[i] = ms[i]; ragBody.sharedMaterials = rs; }
            }
            if (w[3].Length > 0 && BodyKind() != w[3])
            {
                FsmObject o = mat.FsmVariables.FindFsmObject(w[3]);
                Mesh m = o != null ? o.Value as Mesh : null;
                if (m != null) { body.sharedMesh = m; if (ragBody != null) ragBody.sharedMesh = m; changes.Add(w[3]); }
            }
            for (int s = 0; s < AccSlots.Length && accRoot != null; s++)
            {
                Transform slot = accRoot.Find(AccSlots[s]);
                if (slot == null) continue;
                string want = w[4 + s], have = "";
                foreach (Transform x in slot) if (x.gameObject.activeSelf) { have = BaseName(x.name); break; }
                if (want == have) continue;
                foreach (Transform x in slot) Object.Destroy(x.gameObject);
                GameObject model = Model(want);
                if (model != null)
                {
                    GameObject g = (GameObject)Object.Instantiate(model);
                    g.transform.SetParent(slot, false);
                    g.transform.localPosition = Vector3.zero; g.transform.localRotation = Quaternion.identity;
                    g.SetActive(true);
                    // (l'automate les detruit au client suivant : ses variables pointent sur les nouveaux)
                    string var = AccSlots[s] == "Eyewear" ? "Eyewear" : AccSlots[s] == "Cig" ? "Cigarette" : "Hat";
                    FsmGameObject fv = acc != null ? acc.FsmVariables.FindFsmGameObject(var) : null;
                    if (fv != null) fv.Value = g;
                }
                changes.Add(AccSlots[s] + "=" + (want.Length > 0 ? want : "rien"));
            }
            Log.Info("taxi : client de l'hote applique ici (" + string.Join(", ", changes.ToArray()) + ") ; ici avant " + mine);
        }

        // [Test] Autotest=taxiclient : a 30 s chacun allume le client (Customer1, TaxiWalker, Char : son tirage a lui) ; a 45
        // et 60 s chacun note ce qu'il montre. Attendu : la meme apparence des deux cotes (celle de l'hote).
        static int testStep;
        public static void Test(string mode, float t)
        {
            if (mode != "taxiclient") return;
            if (t > 30f && testStep == 0)
            {
                testStep = 1;
                GameObject w = Game.FindAny(WalkerPath);
                for (Transform x = w != null ? w.transform.Find("Char") : null; x != null; x = x.parent) if (!x.gameObject.activeSelf) x.gameObject.SetActive(true);
                Log.Info("autotest : taxiclient, client allume ici");
            }
            if ((t > 45f && testStep == 1) || (t > 60f && testStep == 2)) { testStep++; Log.Info("autotest : " + Describe()); }
        }

        public static string Describe() { return Find() && ch != null ? "taxi : client " + (ch.activeInHierarchy ? Look() : "(inactif)") : "taxi : client introuvable"; }
    }
}
