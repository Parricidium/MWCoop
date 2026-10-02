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
                try { actions = s.Actions; } catch (Exception e) { sb.Append("  {actions illisibles : ").Append(e.GetType().Name).Append('}'); }
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
