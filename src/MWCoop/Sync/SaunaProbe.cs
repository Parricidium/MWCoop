using System.Collections.Generic;
using System.Text;
using HutongGames.PlayMaker;
using UnityEngine;

namespace MWCoop
{
    // Essai Autotest=vapeursonde : ou est la vapeur du sauna (eau jetee sur le poele, retour du 14/10 : « la vapeur du sauna
    // n'apparait pas chez l'autre joueur ») et qui l'allume.
    public static class SaunaProbe
    {
        // Automates et composants sous les poeles (YARD/.../Kiuas, COTTAGE/.../Stove) : etats et actions
        public static string Probe2()
        {
            var sb = new StringBuilder("vapeur2 : ");
            foreach (string root in new[] { "YARD/Building/SAUNA/Sauna/Kiuas", "COTTAGE/Stuff/Sauna/Stove", "YARD/Building/SAUNA/Sauna" })
            {
                GameObject g = Game.FindAny(root);
                if (g == null) { sb.Append(" | ").Append(root).Append(" absent"); continue; }
                foreach (Transform t in g.GetComponentsInChildren<Transform>(true))
                {
                    if (root.EndsWith("Sauna") && t.parent != g.transform && t != g.transform) continue;   // (sauna : premier niveau seulement)
                    var comps = new List<string>();
                    foreach (Component c in t.GetComponents<Component>()) if (c != null && !(c is Transform) && !(c is MeshFilter) && !(c is MeshRenderer)) comps.Add(c.GetType().Name);
                    sb.Append(" | ").Append(Recon.Path(t)).Append(" [").Append(string.Join(",", comps.ToArray())).Append("]");
                    foreach (PlayMakerFSM f in t.GetComponents<PlayMakerFSM>())
                    {
                        sb.Append(" || automate ").Append(f.FsmName).Append(" [").Append(f.ActiveStateName).Append("]").Append(f.enabled ? "" : " (coupe)");
                        foreach (FsmState st in f.FsmStates) sb.Append(" ## ").Append(st.Name).Append(" : ").Append(Jobs.DumpActions(f, st.Name));
                        foreach (FsmEvent ev in f.FsmEvents) sb.Append(" @ev ").Append(ev.Name);
                    }
                }
            }
            return sb.ToString();
        }

        public static string Probe()
        {
            var sb = new StringBuilder("vapeur : ");
            var targets = new List<GameObject>();
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(ParticleEmitter)))
            {
                var pe = o as ParticleEmitter;
                if (pe == null || pe.hideFlags != HideFlags.None) continue;
                string n = pe.name.ToLowerInvariant(), p = Recon.Path(pe.transform).ToLowerInvariant();
                if (!(n.Contains("steam") || n.Contains("loyly") || n.Contains("hoyry") || p.Contains("sauna") || p.Contains("kiuas"))) continue;
                targets.Add(pe.gameObject);
                sb.Append(" | emetteur ").Append(Recon.Path(pe.transform)).Append(pe.gameObject.activeInHierarchy ? " actif" : " inactif").Append(pe.emit ? " emet" : "");
            }
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(ParticleSystem)))
            {
                var ps = o as ParticleSystem;
                if (ps == null || ps.hideFlags != HideFlags.None) continue;
                string n = ps.name.ToLowerInvariant(), p = Recon.Path(ps.transform).ToLowerInvariant();
                if (!(n.Contains("steam") || n.Contains("loyly") || n.Contains("hoyry") || p.Contains("sauna") || p.Contains("kiuas"))) continue;
                targets.Add(ps.gameObject);
                sb.Append(" | systeme ").Append(Recon.Path(ps.transform)).Append(ps.gameObject.activeInHierarchy ? " actif" : " inactif").Append(ps.isPlaying ? " joue" : "");
            }
            // automates qui nomment ces objets (ou leurs parents) dans une action
            var seen = new HashSet<string>();
            foreach (Object o in Game.AllFsms())
            {
                var f = o as PlayMakerFSM;
                if (f == null || f.hideFlags != HideFlags.None) continue;
                FsmState[] sts = null;
                try { sts = f.FsmStates; } catch { }
                if (sts == null) continue;
                foreach (FsmState st in sts)
                {
                    FsmStateAction[] acts = null;
                    try { acts = st.Actions; } catch { }
                    if (acts == null) continue;
                    foreach (FsmStateAction a in acts)
                    {
                        if (a == null) continue;
                        foreach (System.Reflection.FieldInfo fi in a.GetType().GetFields())
                        {
                            object v = null; try { v = fi.GetValue(a); } catch { }
                            GameObject go = null;
                            var od = v as FsmOwnerDefault; if (od != null) go = od.OwnerOption == OwnerDefaultOption.UseOwner ? f.gameObject : od.GameObject != null ? od.GameObject.Value : null;
                            var fg = v as FsmGameObject; if (fg != null) go = fg.Value;
                            if (go == null) continue;
                            foreach (GameObject tg in targets)
                                if (go == tg || tg.transform.IsChildOf(go.transform) && go.transform.parent != null && go.transform.root == tg.transform.root && go.transform != go.transform.root)
                                {
                                    string k = Recon.Path(f.transform) + "::" + f.FsmName + " / " + st.Name + " : " + a.GetType().Name + " -> " + tg.name;
                                    if (seen.Add(k)) sb.Append(" || ").Append(k).Append(f.enabled ? "" : " (coupe)");
                                }
                        }
                    }
                }
            }
            // la louche du joueur
            GameObject throwGo = Game.PlayerPart("Pivot/AnimPivot/Camera/FPSCamera/FPSCamera/SaunaThrow");
            if (throwGo != null)
                foreach (PlayMakerFSM f in throwGo.GetComponents<PlayMakerFSM>())
                {
                    sb.Append(" ||| louche ").Append(f.FsmName).Append(" [").Append(f.ActiveStateName).Append("]");
                    foreach (FsmState st in f.FsmStates) sb.Append(" ## ").Append(st.Name).Append(" : ").Append(Jobs.DumpActions(f, st.Name));
                }
            else sb.Append(" ||| louche introuvable");
            return sb.ToString();
        }
    }
}
