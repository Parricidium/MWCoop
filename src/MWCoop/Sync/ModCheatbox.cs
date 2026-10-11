using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Compatibilite du mod « Cheatbox++ » (demande de JD, 10/10 : « si un joueur fait apparaitre un objet, que tout le monde le
    // voie »). Ses objets sortent des distributeurs du jeu : il envoie SPAWNITEM a l'automate d'un distributeur (magasin :
    // Spawner/CreateItems, CreateItemsSeparate ; pieces : SPAWNERS_VIN, SPAWNERS_Aftermarket, SPAWNERS_Fleetari), puis pose
    // l'objet devant le joueur. MWCoop ne rejouait ces distributeurs que pour les achats a la caisse : l'objet n'existait que
    // chez celui qui l'avait fait apparaitre.
    // Ici : une action en tete de l'etat « Create product » de chaque distributeur repere un appel venu de Cheatbox++ (pile
    // d'appels : PlayMaker traite l'evenement tout de suite) ; a l'image suivante (objet pose), le distributeur et la pose
    // partent aux autres (@cbx), qui font tourner le meme distributeur (meme compteur, meme ID d'objet) et posent l'objet au
    // meme endroit ; Props suit ensuite ses deplacements. Inutile que les autres aient le mod.
    public static class ModCheatbox
    {
        static readonly string[] Holders = { "Spawner/CreateItems", "Spawner/CreateItemsSeparate", "SPAWNERS_VIN", "SPAWNERS_Aftermarket", "SPAWNERS_Fleetari" };
        static readonly HashSet<PlayMakerFSM> hooked = new HashSet<PlayMakerFSM>();
        static readonly Dictionary<string, PlayMakerFSM> byKey = new Dictionary<string, PlayMakerFSM>();
        static readonly List<PlayMakerFSM> pending = new List<PlayMakerFSM>();
        static bool present, looked, replaying;
        static float nextScan;

        public static void OnLevelLoaded() { hooked.Clear(); byKey.Clear(); pending.Clear(); nextScan = 0f; extras.Clear(); extraKeys.Clear(); firstExtra = -1f; staleDone = false; }

        static bool Present()
        {
            if (looked) return present;
            looked = true;
            foreach (System.Reflection.Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = null;
                try { t = a.GetType("CheatboxPlusPlus.Cpp", false); } catch { }
                if (t != null) { present = true; break; }
            }
            if (present) Log.Info("mods : Cheatbox++ present, objets qu'il fait apparaitre partages");
            return present;
        }

        static string KeyOf(PlayMakerFSM f) { return Recon.Path(f.transform) + "::" + f.FsmName; }

        // Essai [Test] Autotest=cbxextra (retour de witefold, 11/10 : « bois et sacs de sable n'apparaissent pas ») : appelle
        // Spawn() des objets du groupe « Extra » de Cheatbox++ (Firewood, Sandbag) comme son bouton, et note ce qui arrive.
        public static string TestExtra()
        {
            const System.Reflection.BindingFlags A = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
            Type cpp = null;
            foreach (System.Reflection.Assembly a in AppDomain.CurrentDomain.GetAssemblies()) { try { cpp = a.GetType("CheatboxPlusPlus.Cpp", false); } catch { } if (cpp != null) break; }
            if (cpp == null) return "Cheatbox++ absent";
            object inst = cpp.GetField("instance", A).GetValue(null);
            object its = inst != null ? cpp.GetField("itemSpawning", A).GetValue(inst) : null;
            if (its == null) return "Cheatbox++ : itemSpawning nul";
            its.GetType().GetMethod("Load", A).Invoke(its, null);
            Array groups = its.GetType().GetField("shopItems", A).GetValue(its) as Array;
            if (groups == null) return "Cheatbox++ : pas de groupes";
            var sb = new System.Text.StringBuilder();
            foreach (object grp in groups)
            {
                if ((string)grp.GetType().GetField("name", A).GetValue(grp) != "Extra") continue;
                foreach (object it in (Array)grp.GetType().GetField("allItems", A).GetValue(grp))
                {
                    string n = (string)it.GetType().GetField("displayName", A).GetValue(it);
                    object pf = it.GetType().GetField("displayPrefab", A).GetValue(it);
                    int before = CountNamed(n == "Firewood" ? "firewood" : "sandbag(cheat)");
                    string err = "";
                    try { it.GetType().GetMethod("Spawn", A).Invoke(it, null); }
                    catch (Exception e) { Exception x = e.InnerException ?? e; err = " ERREUR " + x.GetType().Name + ": " + x.Message; }
                    sb.Append(n).Append(" (modele ").Append(pf != null && !pf.Equals(null) ? "ok" : "ABSENT").Append(") : ")
                      .Append(before).Append(" -> ").Append(CountNamed(n == "Firewood" ? "firewood" : "sandbag(cheat)")).Append(err).Append(" ; ");
                }
            }
            return sb.ToString();
        }
        public static string TestCount()
        {
            var sb = new System.Text.StringBuilder();
            foreach (KeyValuePair<GameObject, string> kv in extras) if (kv.Key != null) sb.Append(kv.Value).Append(" en ").Append(kv.Key.transform.position.ToString("F1")).Append(" ; ");
            return "buches " + CountNamed("firewood(Clone)") + ", sacs " + CountNamed("sandbag(cheat)") + " | " + sb;
        }
        static int CountNamed(string n) { int c = 0; foreach (GameObject g in UnityEngine.Object.FindObjectsOfType<GameObject>()) if (g.name.StartsWith(n) && g.transform.parent == null) c++; return c; }

        class Hook : FsmStateAction
        {
            public PlayMakerFSM F;
            public override void OnEnter()
            {
                try
                {
                    if (!replaying && Session.Active && Session.RemoteCount > 0 && FromCheatbox() && !pending.Contains(F)) pending.Add(F);
                }
                catch (Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        static bool FromCheatbox()
        {
            string st = Environment.StackTrace;
            return st.IndexOf("CheatboxPlusPlus", StringComparison.Ordinal) >= 0;
        }

        static void Scan()
        {
            int n = 0;
            foreach (string h in Holders)
            {
                // (distributeurs de pieces sous CARPARTS/PARTSYSTEM, pas a la racine : cherches par leur nom, comme le mod --
                // avant, « SPAWNERS_VIN introuvable », pieces de Cheatbox++ jamais partagees, retour de witefold 11/10)
                GameObject g = Game.FindAny(h) ?? Game.FindAny("CARPARTS/PARTSYSTEM/" + h) ?? GameObject.Find(h);
                if (g == null) continue;
                foreach (PlayMakerFSM f in g.GetComponentsInChildren<PlayMakerFSM>(true))
                {
                    if (f == null || hooked.Contains(f) || f.Fsm == null) continue;
                    FsmState s = f.Fsm.GetState("Create product");
                    if (s == null || f.FsmVariables.FindFsmGameObject("New") == null) continue;
                    try
                    {
                        var l = new List<FsmStateAction>(s.Actions);
                        l.Insert(0, new Hook { F = f });
                        s.Actions = l.ToArray();
                    }
                    catch { continue; }   // (automate pas encore charge : au prochain passage)
                    hooked.Add(f);
                    byKey[KeyOf(f)] = f;
                    n++;
                }
            }
            if (n > 0) Log.Info("mods : Cheatbox++, " + n + " distributeurs de plus suivis (" + hooked.Count + ")");
        }

        public static void Update()
        {
            if (!Session.Active || !PlayerSync.InGame || !Present()) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextScan) { nextScan = now + (hooked.Count == 0 ? 5f : 60f); Scan(); }
            ExtrasTick(now);
            if (pending.Count == 0) return;
            foreach (PlayMakerFSM f in pending)
            {
                if (f == null) continue;
                FsmGameObject nv = f.FsmVariables.FindFsmGameObject("New");
                GameObject go = nv != null ? nv.Value : null;
                if (go == null) continue;
                Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@cbx").Str(KeyOf(f)).Vec(go.transform.position).Quat(go.transform.rotation), true);
                Log.Info("mods : Cheatbox++, " + go.name + " apparu ici (" + f.FsmName + "), envoye");
            }
            pending.Clear();
        }

        public static void OnRemote(int who, string key, Vector3 pos, Quaternion rot)
        {
            if (byKey.Count == 0 || !byKey.ContainsKey(key)) Scan();
            PlayMakerFSM f;
            if (!byKey.TryGetValue(key, out f) || f == null) { Log.Warn("mods : Cheatbox++, distributeur " + key + " introuvable ici"); return; }
            FsmFloat cond = f.FsmVariables.FindFsmFloat("Condition");
            float was = cond != null ? cond.Value : 0f;
            replaying = true;
            try
            {
                if (cond != null) cond.Value = 100f;   // (comme Cheatbox++ : article neuf)
                f.SendEvent("SPAWNITEM");
            }
            finally { replaying = false; if (cond != null) cond.Value = was; }
            FsmGameObject nv = f.FsmVariables.FindFsmGameObject("New");
            GameObject go = nv != null ? nv.Value : null;
            if (go == null) { Log.Warn("mods : Cheatbox++, rien de cree par " + key); return; }
            go.transform.position = pos; go.transform.rotation = rot;
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb != null) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            Props.Track(go);
            Log.Info("mods : Cheatbox++, " + go.name + " apparu chez #" + who + ", recree ici");
        }

        // ---------------------------------------------------------------- bois et sacs de sable (groupe « Extra »)
        // Retour de witefold (11/10) : « le bois et les sacs de sable n'apparaissent pas ». Cheatbox++ les cree lui-meme
        // (Instantiate du modele « firewood » ; sacs : copie de sandbag(item1) renommee sandbag(cheat), son automate retire),
        // sans distributeur : ils n'existaient que chez celui qui les faisait apparaitre (essai cbxextra : 0 -> 1 chez chacun).
        // Chaque nouveau (racine « firewood(Clone) » / « sandbag(cheat) ») recoit une cle, part aux autres (@cbxx), qui creent
        // le meme au meme endroit ; Props le suit ensuite. Sacs : Cheatbox++ les sauve chez chacun -- ceux de l'hote font foi
        // (renvoyes toutes les 30 s, ajoutes a sa liste s'ils viennent d'un invite) ; ceux qu'un invite recharge de SA
        // sauvegarde sont retires a son arrivee.
        static readonly Dictionary<GameObject, string> extras = new Dictionary<GameObject, string>();
        static readonly HashSet<string> extraKeys = new HashSet<string>();
        static int extraN; static float nextExtra, firstExtra = -1f, nextSnap; static bool staleDone;
        static readonly string session = new System.Random().Next(0x1000, 0xffff).ToString("x");

        static string ExtraKind(GameObject g)
        {
            if (g.transform.parent != null) return null;
            return g.name == "sandbag(cheat)" ? "sac" : g.name == "firewood(Clone)" ? "bois" : null;
        }

        static void ExtrasTick(float now)
        {
            if (now < nextExtra) return;
            nextExtra = now + 1f;
            if (firstExtra < 0f) firstExtra = now;
            int stale = 0;
            foreach (Rigidbody rb in UnityEngine.Object.FindObjectsOfType<Rigidbody>())
            {
                GameObject g = rb.gameObject;
                if (extras.ContainsKey(g)) continue;
                string kind = ExtraKind(g);
                if (kind == null) continue;
                // (buches du jeu -- fendeuse, tas de grumes : memes « firewood(Clone) », deja marquees par Jobs a leur creation)
                if (kind == "bois" && Props.ItemId(g).Length > 0) continue;
                if (!Session.IsHost && !staleDone && kind == "sac") { UnityEngine.Object.Destroy(g); stale++; continue; }   // (de la sauvegarde de l'invite)
                string key = "w:cbx:" + kind + ":" + Session.LocalId + ":" + session + ":" + (++extraN);
                Adopt(g, key);
                SendExtra(kind, key, g.transform);
                Log.Info("mods : Cheatbox++, " + (kind == "sac" ? "sac de sable" : "buche") + " apparu ici, envoye (" + key + ")");
            }
            if (!staleDone) { staleDone = true; if (stale > 0) Log.Info("mods : Cheatbox++, " + stale + " sac(s) de sable de la sauvegarde de l'invite retire(s) (ceux de l'hote font foi)"); }
            if (Session.IsHost && now >= nextSnap && Session.RemoteCount > 0)
            {
                nextSnap = now + 30f;
                foreach (KeyValuePair<GameObject, string> kv in extras) if (kv.Key != null) SendExtra(kv.Value.Split(':')[2], kv.Value, kv.Key.transform);
            }
        }

        static void Adopt(GameObject g, string key) { extras[g] = key; extraKeys.Add(key); Props.Adopt(g, key); }

        static void SendExtra(string kind, string key, Transform t)
        {
            if (Session.RemoteCount == 0) return;
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@cbxx").Str(kind).Str(key).Vec(t.position).Quat(t.rotation), true);
        }

        public static void OnExtra(int who, string kind, string key, Vector3 pos, Quaternion rot)
        {
            if (extraKeys.Contains(key)) return;
            GameObject go = null;
            if (kind == "sac")
            {
                GameObject src = GameObject.Find("sandbag(item1)");
                if (src == null) foreach (GameObject g in Resources.FindObjectsOfTypeAll<GameObject>()) if (g.hideFlags == HideFlags.None && g.name.StartsWith("sandbag(item")) { src = g; break; }
                if (src != null)
                {
                    go = (GameObject)UnityEngine.Object.Instantiate(src);
                    go.name = "sandbag(cheat)";
                    PlayMakerFSM f = go.GetComponent<PlayMakerFSM>(); if (f != null) UnityEngine.Object.Destroy(f);
                    if (!go.activeSelf) go.SetActive(true);
                    if (Session.IsHost) AddToCheatboxSave(go.transform);
                }
            }
            else
            {
                foreach (GameObject g in Resources.FindObjectsOfTypeAll<GameObject>())
                    if (g.hideFlags == HideFlags.None && g.name == "firewood" && g.transform.parent == null && g.activeSelf && !g.activeInHierarchy) { go = (GameObject)UnityEngine.Object.Instantiate(g); break; }
            }
            if (go == null) { Log.Warn("mods : Cheatbox++, modele introuvable ici pour " + kind); return; }
            go.transform.parent = null; go.transform.position = pos; go.transform.rotation = rot;
            Rigidbody rb = go.GetComponent<Rigidbody>(); if (rb != null) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            Adopt(go, key);
            Log.Info("mods : Cheatbox++, " + (kind == "sac" ? "sac de sable" : "buche") + " de #" + who + " recree ici (" + key + ")");
        }

        // Hote : sac recu d'un invite ajoute a la liste de Cheatbox++ (CustomObjects.sandBags), sauvee avec sa partie.
        static void AddToCheatboxSave(Transform t)
        {
            try
            {
                const System.Reflection.BindingFlags A = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
                Type cpp = null;
                foreach (System.Reflection.Assembly a in AppDomain.CurrentDomain.GetAssemblies()) { try { cpp = a.GetType("CheatboxPlusPlus.Cpp", false); } catch { } if (cpp != null) break; }
                object inst = cpp != null ? cpp.GetField("instance", A).GetValue(null) : null;
                object its = inst != null ? cpp.GetField("itemSpawning", A).GetValue(inst) : null;
                object co = its != null ? its.GetType().GetField("customObjects", A).GetValue(its) : null;
                var list = co != null ? co.GetType().GetField("sandBags", A).GetValue(co) as System.Collections.IList : null;
                if (list != null) list.Add(t);
            }
            catch (Exception e) { Log.Warn("mods : Cheatbox++, sac non ajoute a sa sauvegarde : " + e.Message); }
        }

        // [Test] Autotest=cbx : l'hote fait apparaitre par Cheatbox++ (reflexion : SpawnerItem.UseSpawner) l'article [Test]
        // CbxArticle (automate de Spawner/CreateItems, « Beer » par defaut) a 30 s ; chacun note les objets de ce nom pres de
        // l'hote a 40 s.
        static int testStep;
        static PlayMakerFSM PartSpawner(string dist, string art)
        {
            GameObject g = Game.FindAny("CARPARTS/PARTSYSTEM/" + dist) ?? GameObject.Find(dist);
            if (g == null) return null;
            foreach (Transform c in g.transform)
            {
                PlayMakerFSM f = c.GetComponent<PlayMakerFSM>();
                if (f != null && f.FsmVariables.FindFsmGameObject("New") != null && (art.Length == 0 || art == "Beer" || c.name == art)) return f;
            }
            return null;
        }

        static void TestPart(string dist, string art, float t)
        {
            PlayMakerFSM sp = PartSpawner(dist, art);
            if (Session.IsHost && t > 30f && testStep == 0)
            {
                testStep = 1;
                Type tt = null;
                foreach (System.Reflection.Assembly a in AppDomain.CurrentDomain.GetAssemblies()) { try { tt = tt ?? a.GetType("CheatboxPlusPlus.SpawnerItem", false); } catch { } }
                System.Reflection.MethodInfo m = tt != null ? tt.GetMethod("UseSpawner", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public) : null;
                if (sp == null || m == null) Log.Info("autotest : cbx, " + (sp == null ? "pas de distributeur sous " + dist : "Cheatbox++ absent"));
                else { m.Invoke(null, new object[] { sp, null }); Log.Info("autotest : cbx, piece de " + sp.gameObject.name + " demandee a Cheatbox++"); }
            }
            if (t > 40f && testStep == 1 || !Session.IsHost && t > 50f && testStep == 0)
            {
                testStep = 2;
                FsmGameObject nv = sp != null ? sp.FsmVariables.FindFsmGameObject("New") : null;
                GameObject go = nv != null ? nv.Value : null;
                Log.Info("autotest : cbx, derniere piece de " + (sp != null ? sp.gameObject.name : "?") + " creee ici : " + (go != null ? go.name + " en " + go.transform.position.ToString("F1") : "aucune"));
            }
        }

        public static void Test(string mode, float t)
        {
            if (mode != "cbx") return;
            string art = Config.Get("Test", "CbxArticle", "Beer");
            // [Test] CbxDistrib=SPAWNERS_VIN : un distributeur de pieces (enfant nomme CbxArticle, sinon le premier enfant).
            string dist = Config.Get("Test", "CbxDistrib", "");
            if (dist.Length > 0) { TestPart(dist, art, t); return; }
            if (Session.IsHost && t > 30f && testStep == 0)
            {
                testStep = 1;
                GameObject g = Game.FindAny("Spawner/CreateItems");
                PlayMakerFSM sp = null;
                if (g != null) foreach (PlayMakerFSM f in g.GetComponents<PlayMakerFSM>()) if (f.FsmName == art) sp = f;
                Type tt = null;
                foreach (System.Reflection.Assembly a in AppDomain.CurrentDomain.GetAssemblies()) { try { tt = tt ?? a.GetType("CheatboxPlusPlus.SpawnerItem", false); } catch { } }
                System.Reflection.MethodInfo m = tt != null ? tt.GetMethod("UseSpawner", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public) : null;
                if (sp == null && g != null) { var names = new List<string>(); foreach (PlayMakerFSM f in g.GetComponents<PlayMakerFSM>()) names.Add(f.FsmName); Log.Info("autotest : cbx, automates : " + string.Join(", ", names.ToArray())); }
                if (sp == null || m == null) Log.Info("autotest : cbx, " + (sp == null ? "pas d'automate " + art : "Cheatbox++ absent"));
                else { m.Invoke(null, new object[] { sp, null }); Log.Info("autotest : cbx, " + art + " demande a Cheatbox++"); }
            }
            if (t > 40f && testStep == 1 || !Session.IsHost && t > 50f && testStep == 0)
            {
                testStep = 2;
                FsmGameObject nv = null;
                GameObject g = Game.FindAny("Spawner/CreateItems");
                if (g != null) foreach (PlayMakerFSM f in g.GetComponents<PlayMakerFSM>()) if (f.FsmName == art) nv = f.FsmVariables.FindFsmGameObject("New");
                GameObject go = nv != null ? nv.Value : null;
                Log.Info("autotest : cbx, dernier " + art + " cree ici : " + (go != null ? go.name + " en " + go.transform.position.ToString("F1") : "aucun"));
            }
        }
    }
}
