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

        public static void OnLevelLoaded() { hooked.Clear(); byKey.Clear(); pending.Clear(); nextScan = 0f; }

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
                GameObject g = Game.FindAny(h);
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

        // [Test] Autotest=cbx : l'hote fait apparaitre par Cheatbox++ (reflexion : SpawnerItem.UseSpawner) l'article [Test]
        // CbxArticle (automate de Spawner/CreateItems, « Beer » par defaut) a 30 s ; chacun note les objets de ce nom pres de
        // l'hote a 40 s.
        static int testStep;
        public static void Test(string mode, float t)
        {
            if (mode != "cbx") return;
            string art = Config.Get("Test", "CbxArticle", "Beer");
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
