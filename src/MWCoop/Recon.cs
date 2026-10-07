using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using HutongGames.PlayMaker;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MWCoop
{
    // Vidage lisible d'un niveau : arbre des objets (inactifs compris), composants, automates
    // PlayMaker (etat actif, etats, transitions, actions avec leurs parametres, variables).
    // Sert a la reconnaissance, jamais en partie normale.
    public static class Recon
    {
        public static string DumpLevel(string tag, bool actionParams)
        {
            string dir = System.IO.Path.Combine(Log.DataDir, "dumps");
            Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, Application.loadedLevelName + "-" + tag + ".txt");
            var sb = new StringBuilder();
            foreach (GameObject go in SceneRoots())
                Walk(sb, go.transform, 0, actionParams);
            File.WriteAllText(path, sb.ToString());
            Log.Info("vidage : " + path);
            return path;
        }

        // Releve de TOUS les automates du monde que personne ne suit encore : persistant (cle de
        // sauvegarde UT/UniqueTag), commande du joueur (clic, touche, molette), personnel (agit sur le
        // joueur : objets sous PLAYER, globales Player*), argent (PlayerMoney). dumps/monde.txt.
        public static string DumpWorldFsms()
        {
            var lines = new List<string>();
            var perRoot = new SortedDictionary<string, int[]>();
            foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var f = (PlayMakerFSM)o;
                if (f.hideFlags != HideFlags.None) continue;
                string root = f.transform.root.name;
                if (root == "PLAYER" || root == "GUI" || root.StartsWith("MWCoop")) continue;
                if (Interactions.Tracks(f) || Jobs.Tracks(f) || CarDoors.Tracks(f) || Consume.Tracks(f)) continue;
                bool persist = false, input = false, personal = false, money = false, playerGlobal = false;
                foreach (FsmString s in f.FsmVariables.StringVariables)
                    if (s.Name.StartsWith("UniqueTag") || s.Name.StartsWith("UT")) persist = true;
                try
                {
                    foreach (FsmState st in f.Fsm.States)
                        foreach (FsmStateAction a in st.Actions)
                        {
                            string tn = a.GetType().Name;
                            if (tn == "MousePickEvent" || tn == "GetButtonDown" || tn == "GetButtonUp" || tn == "GetMouseButtonDown" || tn == "GetAxis" || tn == "GetKeyDown") input = true;
                            foreach (FieldInfo fi in a.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                            {
                                object v = fi.GetValue(a);
                                GameObject go = null;
                                if (v is FsmGameObject) go = ((FsmGameObject)v).Value;
                                else if (v is FsmOwnerDefault) { var od = (FsmOwnerDefault)v; go = od.OwnerOption == OwnerDefaultOption.UseOwner ? f.gameObject : od.GameObject.Value; }
                                if (go != null && go.transform.root.name == "PLAYER") personal = true;
                                var nv = v as NamedVariable;
                                if (nv != null && nv.UseVariable && nv.Name.StartsWith("Player") && !Game.LocalVar(f, nv.Name))
                                {
                                    playerGlobal = true;
                                    if (nv.Name == "PlayerMoney") money = true;
                                }
                            }
                        }
                }
                catch { }
                string flags = (persist ? "P" : "-") + (input ? "I" : "-") + (personal ? "J" : "-") + (playerGlobal ? "G" : "-") + (money ? "$" : "-");
                int[] c;
                if (!perRoot.TryGetValue(root, out c)) perRoot[root] = c = new int[4];
                c[0]++; if (persist) c[1]++; if (input) c[2]++; if (personal || playerGlobal) c[3]++;
                if (persist || input) lines.Add(root + " | " + flags + " | " + Path(f.transform) + " :: " + f.FsmName + " [" + f.ActiveStateName + "]");
            }
            lines.Sort(string.CompareOrdinal);
            var sb = new StringBuilder("P=persistant I=commande J=agit sur le joueur G=globales Player* $=argent\n\nPAR RACINE (total, persistants, commandes, personnels)\n");
            foreach (KeyValuePair<string, int[]> kv in perRoot) sb.Append(kv.Key).Append(" : ").Append(kv.Value[0]).Append(", ").Append(kv.Value[1]).Append(", ").Append(kv.Value[2]).Append(", ").Append(kv.Value[3]).Append('\n');
            sb.Append("\nAUTOMATES PERSISTANTS OU COMMANDES NON SUIVIS (").Append(lines.Count).Append(")\n");
            foreach (string l in lines) sb.Append(l).Append('\n');
            string dir = System.IO.Path.Combine(Log.DataDir, "dumps");
            Directory.CreateDirectory(dir);
            string file = System.IO.Path.Combine(dir, "monde.txt");
            File.WriteAllText(file, sb.ToString());
            return file + " (" + lines.Count + " a regarder)";
        }

        // Releve d'une racine ([Test] RacineReleve, defaut RACES) : arbre sur 5 niveaux (actif ou non, composants),
        // puis chaque corps physique (composants : voiture, IA...) et chaque automate (etat, module qui le tient,
        // P persistant / I commande du joueur, etats et evenements). dumps/racine-<nom>.txt.
        public static string DumpRoot(string rootName)
        {
            GameObject root = Game.FindAny(rootName);
            if (root == null) return rootName + " introuvable";
            var sb = new StringBuilder("ARBRE " + rootName + "\n");
            Tree(sb, root.transform, 0, 5);
            sb.Append("\nCORPS PHYSIQUES\n");
            foreach (Rigidbody rb in root.GetComponentsInChildren<Rigidbody>(true))
            {
                sb.Append(Path(rb.transform)).Append(rb.gameObject.activeInHierarchy ? "" : " (inactif)").Append(rb.isKinematic ? " cinematique" : "").Append(" :");
                foreach (Component c in rb.GetComponents<Component>()) if (c != null) sb.Append(' ').Append(c.GetType().Name);
                sb.Append('\n');
            }
            sb.Append("\nAUTOMATES\n");
            foreach (PlayMakerFSM f in root.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                string owner = Replay.Owner(f);
                bool persist = false, input = false;
                foreach (FsmString s in f.FsmVariables.StringVariables) if (s.Name.StartsWith("UniqueTag") || s.Name.StartsWith("UT")) persist = true;
                var states = new List<string>();
                try
                {
                    if (f.Fsm.States.Length > 0 && !f.Fsm.States[0].IsInitialized) f.Fsm.InitData();
                    foreach (FsmState st in f.Fsm.States)
                    {
                        var tr = new List<string>();
                        foreach (FsmTransition t in st.Transitions) tr.Add(t.EventName + ">" + t.ToState);
                        states.Add(st.Name + (tr.Count > 0 ? "[" + string.Join(",", tr.ToArray()) + "]" : ""));
                        foreach (FsmStateAction a in st.Actions)
                        {
                            string tn = a != null ? a.GetType().Name : "";
                            if (tn == "MousePickEvent" || tn == "GetButtonDown" || tn == "GetMouseButtonDown" || tn == "GetKeyDown") input = true;
                        }
                    }
                }
                catch { states.Add("(illisible)"); }
                var globals = new List<string>();
                foreach (FsmTransition t in f.Fsm.GlobalTransitions) globals.Add(t.EventName + ">" + t.ToState);
                sb.Append(Path(f.transform)).Append(" :: ").Append(f.FsmName).Append(" [").Append(f.ActiveStateName).Append("]")
                  .Append(f.gameObject.activeInHierarchy ? "" : " (inactif)").Append(persist ? " P" : "").Append(input ? " I" : "")
                  .Append(" <").Append(owner ?? "libre").Append(">")
                  .Append(globals.Count > 0 ? " globaux " + string.Join(",", globals.ToArray()) : "")
                  .Append(" : ").Append(string.Join(" | ", states.ToArray())).Append('\n');
            }
            string dir = System.IO.Path.Combine(Log.DataDir, "dumps");
            Directory.CreateDirectory(dir);
            string file = System.IO.Path.Combine(dir, "racine-" + rootName + ".txt");
            File.WriteAllText(file, sb.ToString());
            return file;
        }

        static void Tree(StringBuilder sb, Transform t, int depth, int max)
        {
            sb.Append(new string(' ', depth * 2)).Append(t.name).Append(t.gameObject.activeSelf ? "" : " (off)");
            foreach (Component c in t.GetComponents<Component>())
            {
                if (c == null || c is Transform) continue;
                string n = c.GetType().Name;
                if (n == "MeshFilter" || n == "MeshRenderer" || n.EndsWith("Collider")) continue;
                sb.Append(" <").Append(n == "PlayMakerFSM" ? "FSM:" + ((PlayMakerFSM)c).FsmName : n).Append('>');
            }
            sb.Append('\n');
            if (depth >= max) { if (t.childCount > 0) sb.Append(new string(' ', depth * 2 + 2)).Append("... ").Append(t.childCount).Append(" enfants\n"); return; }
            for (int i = 0; i < t.childCount; i++) Tree(sb, t.GetChild(i), depth + 1, max);
        }

        // Releve de l'argent : chaque etat d'automate (inactifs compris) dont une action touche PlayerMoney ou
        // PlayerBankAccount, avec l'action, le champ, la valeur ajoutee et le module qui tient l'automate.
        // dumps/argent.txt.
        public static string DumpMoney()
        {
            var lines = new List<string>();
            foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var f = (PlayMakerFSM)o;
                if (f.hideFlags != HideFlags.None) continue;
                try
                {
                    FsmState[] states = f.Fsm.States;
                    if (states.Length > 0 && !states[0].IsInitialized) f.Fsm.InitData();
                    foreach (FsmState st in f.Fsm.States)
                        foreach (FsmStateAction a in st.Actions)
                        {
                            if (a == null) continue;
                            var hits = new List<string>();
                            string other = "";
                            foreach (FieldInfo fi in a.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                            {
                                object v = fi.GetValue(a);
                                var nv = v as NamedVariable;
                                if (nv != null && nv.UseVariable && (nv.Name == "PlayerMoney" || nv.Name == "PlayerBankAccount") && !Game.LocalVar(f, nv.Name))
                                    hits.Add(fi.Name + "=" + nv.Name);
                                else if (v is FsmFloat) { var ff = (FsmFloat)v; other += " " + fi.Name + "=" + (ff.UseVariable ? "{" + ff.Name + "}" : "") + ff.Value; }
                            }
                            if (hits.Count == 0) continue;
                            string owner = Replay.Owner(f);
                            lines.Add(f.transform.root.name + " | " + Path(f.transform) + " :: " + f.FsmName + " / " + st.Name + " : " + a.GetType().Name
                                      + "(" + string.Join(", ", hits.ToArray()) + other + ")" + (owner != null ? " [" + owner + "]" : " [libre]")
                                      + (f.gameObject.activeInHierarchy ? "" : " (inactif)"));
                        }
                }
                catch (System.Exception e) { lines.Add(Path(f.transform) + " :: " + f.FsmName + " : illisible (" + e.GetType().Name + ")"); }
            }
            lines.Sort(string.CompareOrdinal);
            string dir = System.IO.Path.Combine(Log.DataDir, "dumps");
            Directory.CreateDirectory(dir);
            string file = System.IO.Path.Combine(dir, "argent.txt");
            File.WriteAllText(file, string.Join("\n", lines.ToArray()) + "\n");
            return file + " (" + lines.Count + " actions)";
        }

        // Releve des objets cliquables (MousePickEvent / bouton Use) et de qui les suit deja :
        // dumps/interactifs.txt, les non suivis d'abord, regroupes par nom d'objet et d'automate.
        public static string DumpInteractive()
        {
            var untracked = new SortedDictionary<string, List<string>>();
            var tracked = new SortedDictionary<string, int>();
            foreach (PlayMakerFSM f in Object.FindObjectsOfType<PlayMakerFSM>())
            {
                string root = f.transform.root.name;
                if (root == "PLAYER" || root == "GUI") continue;
                bool clicky = false;
                try
                {
                    foreach (FsmState s in f.Fsm.States)
                    {
                        foreach (FsmStateAction a in s.Actions)
                        {
                            string n = a.GetType().Name;
                            if (n == "MousePickEvent" || n == "GetButtonDown" || n == "GetButtonUp" || n == "GetMouseButtonDown") { clicky = true; break; }
                        }
                        if (clicky) break;
                    }
                }
                catch { }
                if (!clicky) continue;
                string who = Interactions.Tracks(f) ? "interactions" : Jobs.Tracks(f) ? "progression" : Consume.Tracks(f) ? "consommables" : null;
                string key = f.gameObject.name + " :: " + f.FsmName;
                if (who != null) { int c; tracked.TryGetValue(who + " : " + key, out c); tracked[who + " : " + key] = c + 1; continue; }
                List<string> l;
                if (!untracked.TryGetValue(key, out l)) untracked[key] = l = new List<string>();
                if (l.Count < 3) l.Add(Path(f.transform) + " [" + f.ActiveStateName + "]");
                else if (l.Count == 3) l.Add("...");
            }
            var sb = new StringBuilder("NON SUIVIS (" + untracked.Count + ")\n");
            foreach (KeyValuePair<string, List<string>> kv in untracked)
            {
                sb.Append(kv.Key).Append('\n');
                foreach (string p in kv.Value) sb.Append("    ").Append(p).Append('\n');
            }
            sb.Append("\nDEJA SUIVIS\n");
            foreach (KeyValuePair<string, int> kv in tracked) sb.Append(kv.Value).Append(" x ").Append(kv.Key).Append('\n');
            string dir = System.IO.Path.Combine(Log.DataDir, "dumps");
            Directory.CreateDirectory(dir);
            string file = System.IO.Path.Combine(dir, "interactifs.txt");
            File.WriteAllText(file, sb.ToString());
            return file + " (" + untracked.Count + " non suivis)";
        }

        // Sous-arbres choisis ([Test] VidageCibles=chemin1;chemin2), avec les parametres des actions.
        public static string DumpTargets(string list)
        {
            var sb = new StringBuilder();
            foreach (string path in list.Split(';'))
            {
                if (path.Trim().Length == 0) continue;
                if (path.StartsWith("pres:") || path.StartsWith("presp:"))
                {
                    bool withParams = path.StartsWith("presp:");
                    // Objets physiques a moins de N m du joueur, avec leurs automates (sans actions).
                    float rad = float.Parse(path.Substring(path.IndexOf(':') + 1), System.Globalization.CultureInfo.InvariantCulture);
                    Vector3 c = GameObject.Find("PLAYER").transform.position;
                    foreach (Rigidbody rb in Object.FindObjectsOfType<Rigidbody>())
                        if ((rb.position - c).sqrMagnitude < rad * rad && rb.transform.root.name != "PLAYER")
                        {
                            sb.Append("===== ").Append(Path(rb.transform)).Append('\n');
                            Walk(sb, rb.transform, 0, withParams);
                        }
                    continue;
                }
                GameObject go = path.StartsWith("id:") ? Parts.FindById(path.Substring(3).Trim()) : Game.FindAny(path.Trim());
                sb.Append("===== ").Append(path).Append(go == null ? " : introuvable\n" : "\n");
                if (go != null) Walk(sb, go.transform, 0, true);
            }
            string dir = System.IO.Path.Combine(Log.DataDir, "dumps");
            Directory.CreateDirectory(dir);
            string p = System.IO.Path.Combine(dir, "cibles.txt");
            File.WriteAllText(p, sb.ToString());
            return p;
        }

        // Racines de la scene, inactives comprises (FindObjectsOfType ne voit que les actives).
        // Unity 5.0 n'a pas GameObject.scene : les modeles (prefabs) charges en memoire sortent aussi.
        public static List<GameObject> SceneRoots()
        {
            var list = new List<GameObject>();
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(GameObject)))
            {
                var go = (GameObject)o;
                if (go.transform.parent == null && go.hideFlags == HideFlags.None) list.Add(go);
            }
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return list;
        }

        static void Walk(StringBuilder sb, Transform t, int depth, bool actionParams)
        {
            string pad = new string(' ', depth * 2);
            GameObject go = t.gameObject;
            sb.Append(pad).Append(go.activeSelf ? "" : "[off] ").Append(go.name)
              .Append("  @").Append(t.position.ToString("F1"));
            foreach (Component c in go.GetComponents<Component>())
                if (c != null && !(c is Transform)) sb.Append("  <").Append(c.GetType().Name).Append('>');
            sb.Append('\n');
            foreach (PlayMakerFSM f in go.GetComponents<PlayMakerFSM>())
                AppendFsm(sb, pad + "  ", f, actionParams);
            if (actionParams)
            {
                foreach (PlayMakerHashTableProxy h in go.GetComponents<PlayMakerHashTableProxy>())
                {
                    sb.Append(pad).Append("  # table '").Append(h.referenceName).Append("' ");
                    int n = 0;
                    if (h._hashTable != null)
                        foreach (System.Collections.DictionaryEntry e in h._hashTable) { if (n++ < 12) sb.Append(e.Key).Append('=').Append(e.Value).Append(' '); }
                    sb.Append("(").Append(n).Append(")\n");
                }
                foreach (PlayMakerArrayListProxy l in go.GetComponents<PlayMakerArrayListProxy>())
                    sb.Append(pad).Append("  # liste '").Append(l.referenceName).Append("' (").Append(l._arrayList != null ? l._arrayList.Count : -1).Append(")\n");
            }
            foreach (Transform c in t) Walk(sb, c, depth + 1, actionParams);
        }

        public static void AppendFsm(StringBuilder sb, string pad, PlayMakerFSM f, bool actionParams)
        {
            sb.Append(pad).Append("* FSM '").Append(f.FsmName).Append("' etat=").Append(f.ActiveStateName).Append('\n');
            Fsm fsm = f.Fsm;
            foreach (FsmState s in fsm.States)
            {
                sb.Append(pad).Append("    ").Append(s.Name).Append(" :");
                foreach (FsmTransition tr in s.Transitions)
                    sb.Append(' ').Append(tr.EventName).Append("->").Append(tr.ToState);
                FsmStateAction[] actions = null;
                try { actions = s.Actions; }
                catch (Exception)
                {
                    // Automate jamais demarre (objet inactif) : ses donnees n'ont pas ete chargees.
                    try { fsm.InitData(); actions = s.Actions; }
                    catch (Exception e) { sb.Append("  {actions illisibles : ").Append(e.GetType().Name).Append('}'); }
                }
                if (actions == null) { sb.Append('\n'); continue; }
                if (!actionParams)
                {
                    sb.Append("  {");
                    foreach (FsmStateAction a in actions) if (a != null) sb.Append(a.GetType().Name).Append(' ');
                    sb.Append("}\n");
                    continue;
                }
                sb.Append('\n');
                foreach (FsmStateAction a in actions)
                    if (a != null) sb.Append(pad).Append("        ").Append(Describe(a)).Append('\n');
            }
            foreach (FsmTransition tr in fsm.GlobalTransitions)
                sb.Append(pad).Append("    global ").Append(tr.EventName).Append("->").Append(tr.ToState).Append('\n');
            AppendVars(sb, pad + "    var ", fsm.Variables);
        }

        // "ActivateGameObject(gameObject=Menu/NewGame, activate=True, ...)".
        public static string Describe(FsmStateAction a)
        {
            var sb = new StringBuilder(a.GetType().Name).Append('(');
            bool first = true;
            foreach (FieldInfo fi in a.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (fi.DeclaringType == typeof(FsmStateAction)) continue;
                string v;
                try { v = Value(fi.GetValue(a)); } catch (Exception e) { v = "?" + e.GetType().Name; }
                if (v == null) continue;
                if (!first) sb.Append(", ");
                first = false;
                sb.Append(fi.Name).Append('=').Append(v);
            }
            return sb.Append(')').ToString();
        }

        static string Value(object o)
        {
            if (o == null) return null;
            var owner = o as FsmOwnerDefault;
            if (owner != null)
                return owner.OwnerOption == OwnerDefaultOption.UseOwner ? "(soi)" : Value(owner.GameObject);
            var ev = o as FsmEvent;
            if (ev != null) return "evt:" + ev.Name;
            var fp = o as FsmProperty;
            if (fp != null)
            {
                // Propriete visee : objet, nom, et valeur (lue ou ecrite).
                string val = "";
                foreach (FieldInfo f in typeof(FsmProperty).GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!f.Name.EndsWith("Parameter")) continue;
                    var pv = f.GetValue(fp) as NamedVariable;
                    if (pv != null && (!string.IsNullOrEmpty(pv.Name) || pv.ToString() != "" && pv.ToString() != "0" && pv.ToString() != "False" && pv.ToString() != "None" && pv.ToString() != "(0.0, 0.0, 0.0)"))
                        val += " " + f.Name.Replace("Parameter", "") + "=" + Value(pv);
                }
                return "prop(" + Value(fp.TargetObject) + " ." + fp.PropertyName + (fp.setProperty ? " :=" : " ->") + val + ")";
            }
            var nv = o as NamedVariable;
            if (nv != null)
            {
                string val;
                var g = nv as FsmGameObject;
                if (g != null) val = g.Value != null ? Path(g.Value.transform) : "null";
                else val = nv.ToString();
                return string.IsNullOrEmpty(nv.Name) ? val : "{" + nv.Name + "}=" + val;
            }
            var uo = o as Object;
            if (uo != null) return uo is GameObject ? Path(((GameObject)uo).transform) : uo.name;
            var arr = o as Array;
            if (arr != null)
            {
                var sb = new StringBuilder("[");
                int n = 0;
                foreach (object e in arr) { if (n++ > 0) sb.Append("; "); if (n > 12) { sb.Append("..."); break; } sb.Append(Value(e)); }
                return sb.Append(']').ToString();
            }
            return o.ToString();
        }

        public static string Path(Transform t)
        {
            string p = t.name;
            for (Transform c = t.parent; c != null; c = c.parent) p = c.name + "/" + p;
            return p;
        }

        static void AppendVars(StringBuilder sb, string pad, FsmVariables v)
        {
            foreach (NamedVariable n in v.GetAllNamedVariables())
            {
                string val;
                try { val = Value(n); } catch { val = "?"; }
                sb.Append(pad).Append(n.GetType().Name).Append(' ').Append(val).Append('\n');
            }
        }

        // Personnages : chaque bodymesh (SkinnedMeshRenderer), son chemin, l'Animation la plus
        // proche au-dessus et ses clips. Sert a choisir les modeles des joueurs distants.
        public static string DumpCharacters()
        {
            var sb = new StringBuilder();
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(SkinnedMeshRenderer)))
            {
                var smr = (SkinnedMeshRenderer)o;
                if (smr.hideFlags != HideFlags.None || smr.sharedMesh == null) continue;
                if (smr.bones == null || smr.bones.Length < 10) continue;
                Transform t = smr.transform;
                Animation anim = null;
                for (Transform p = t; p != null && anim == null; p = p.parent)
                {
                    anim = p.GetComponent<Animation>();
                    if (anim == null) foreach (Animation a in p.GetComponentsInChildren<Animation>(true)) { anim = a; break; }
                }
                sb.Append(Path(t)).Append("  mesh=").Append(smr.sharedMesh.name)
                  .Append(" os=").Append(smr.bones.Length)
                  .Append(" mat=").Append(smr.sharedMaterial != null ? smr.sharedMaterial.name : "-")
                  .Append(" actif=").Append(t.gameObject.activeInHierarchy).Append('\n');
                if (anim != null)
                {
                    sb.Append("    Animation sur ").Append(Path(anim.transform)).Append(" :");
                    foreach (AnimationState st in anim) sb.Append(' ').Append(st.name).Append('(').Append(st.length.ToString("F1")).Append(')');
                    sb.Append('\n');
                }
            }
            string dir = System.IO.Path.Combine(Log.DataDir, "dumps");
            Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, "characters.txt");
            File.WriteAllText(path, sb.ToString());
            return path;
        }

        // Apparences des personnages (corpulence, accessoires pour les avatars) : maillages de corps distincts
        // (sommets, taille, os), puis tout ce qui est accroche aux squelettes des PNJ en plus du corps (chapeaux,
        // cheveux, lunettes...) : maillage, os porteur, matiere, combien de PNJ. dumps/apparences.txt.
        public static string DumpLooks()
        {
            var sb = new StringBuilder("CORPS (SkinnedMeshRenderer a 10 os ou plus, par maillage)\n");
            var bodies = new Dictionary<Mesh, List<SkinnedMeshRenderer>>();
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(SkinnedMeshRenderer)))
            {
                var smr = (SkinnedMeshRenderer)o;
                if (smr.hideFlags != HideFlags.None || smr.sharedMesh == null || smr.bones == null || smr.bones.Length < 10) continue;
                List<SkinnedMeshRenderer> l;
                if (!bodies.TryGetValue(smr.sharedMesh, out l)) bodies[smr.sharedMesh] = l = new List<SkinnedMeshRenderer>();
                l.Add(smr);
            }
            var chars = new HashSet<Transform>();
            foreach (KeyValuePair<Mesh, List<SkinnedMeshRenderer>> kv in bodies)
            {
                Mesh m = kv.Key;
                var mats = new HashSet<string>();
                foreach (SkinnedMeshRenderer s in kv.Value) if (s.sharedMaterial != null) mats.Add(s.sharedMaterial.name.Replace(" (Instance)", ""));
                sb.Append(m.name).Append(" #").Append(m.GetInstanceID()).Append(" : ").Append(m.vertexCount).Append(" sommets, taille ").Append(m.bounds.size.ToString("F2"))
                  .Append(", ").Append(kv.Value[0].bones.Length).Append(" os, ").Append(m.blendShapeCount).Append(" formes, ").Append(kv.Value.Count).Append(" PNJ\n");
                for (int i = 0; i < kv.Value.Count && i < 6; i++) sb.Append("    ").Append(Path(kv.Value[i].transform)).Append('\n');
                sb.Append("    matieres : ").Append(string.Join(", ", new List<string>(mats).ToArray())).Append('\n');
                var sets = new HashSet<string>();   // toutes les matieres de chaque PNJ (sous-maillages : chemise, pantalon, visage ?)
                foreach (SkinnedMeshRenderer s in kv.Value)
                {
                    var names = new List<string>();
                    foreach (Material x in s.sharedMaterials) names.Add(x != null ? x.name.Replace(" (Instance)", "") : "-");
                    sets.Add(string.Join("+", names.ToArray()));
                }
                sb.Append("    sous-maillages ").Append(m.subMeshCount).Append(", jeux : ").Append(string.Join(" ; ", new List<string>(sets).ToArray())).Append('\n');
                foreach (SkinnedMeshRenderer s in kv.Value) if (s.transform.parent != null) chars.Add(s.transform.parent);
            }
            sb.Append("\nACCROCHE AUX PERSONNAGES (hors corps), par maillage et os porteur\n");
            var acc = new SortedDictionary<string, List<string>>();
            foreach (Transform ch in chars)
                foreach (Renderer r in ch.GetComponentsInChildren<Renderer>(true))
                {
                    var smr = r as SkinnedMeshRenderer;
                    if (smr != null && smr.bones != null && smr.bones.Length >= 10) continue;
                    MeshFilter mf = r.GetComponent<MeshFilter>();
                    Mesh mesh = smr != null ? smr.sharedMesh : mf != null ? mf.sharedMesh : null;
                    string bone = r.transform.parent != null ? r.transform.parent.name : "-";
                    string key = (mesh != null ? mesh.name + " (" + mesh.vertexCount + " sommets)" : r.GetType().Name) + " sur " + bone + " | " + r.gameObject.name
                               + " | " + (r.sharedMaterial != null ? r.sharedMaterial.name.Replace(" (Instance)", "") : "-");
                    List<string> l;
                    if (!acc.TryGetValue(key, out l)) acc[key] = l = new List<string>();
                    l.Add(Path(ch) + (r.gameObject.activeInHierarchy ? "" : " (inactif)"));
                }
            foreach (KeyValuePair<string, List<string>> kv in acc)
            {
                sb.Append(kv.Key).Append(" : ").Append(kv.Value.Count).Append('\n');
                for (int i = 0; i < kv.Value.Count && i < 4; i++) sb.Append("    ").Append(kv.Value[i]).Append('\n');
            }
            sb.Append("\nMATIERES char_* (texture, taille)\n");
            var seen = new SortedDictionary<string, string>();
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(Material)))
            {
                var mt = (Material)o;
                if (!mt.name.StartsWith("char_") || mt.name.Contains("(Instance)")) continue;
                Texture tx = mt.mainTexture;
                seen[mt.name] = tx != null ? tx.name + " " + tx.width + "x" + tx.height : "-";
            }
            foreach (KeyValuePair<string, string> kv in seen) sb.Append(kv.Key).Append(" : ").Append(kv.Value).Append('\n');
            string dir = System.IO.Path.Combine(Log.DataDir, "dumps");
            Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, "apparences.txt");
            File.WriteAllText(path, sb.ToString());
            return path;
        }

        // Tous les clips d'animation charges : nom, duree, boucle. Pour choisir ceux des avatars.
        public static string DumpClips()
        {
            var names = new List<string>();
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(AnimationClip)))
            {
                var c = (AnimationClip)o;
                names.Add(c.name + " (" + c.length.ToString("F1") + " s, " + c.wrapMode + (c.legacy ? "" : ", mecanim") + ")");
            }
            names.Sort();
            string dir = System.IO.Path.Combine(Log.DataDir, "dumps");
            Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, "clips.txt");
            File.WriteAllText(path, string.Join("\n", names.ToArray()));
            return path + " (" + names.Count + ")";
        }

        public static string DumpGlobals()
        {
            string dir = System.IO.Path.Combine(Log.DataDir, "dumps");
            Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, "globals.txt");
            var sb = new StringBuilder();
            AppendVars(sb, "", FsmVariables.GlobalVariables);
            File.WriteAllText(path, sb.ToString());
            return path;
        }
    }
}
