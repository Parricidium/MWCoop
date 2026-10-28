using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace MWCoop
{
    // Releve des composants UnityCar d'une voiture du jeu (pilote « addon standalone », spec du 28/10 : la R5 Maxi construite par
    // notre code a partir des composants du jeu -- CarDynamics, Drivetrain, Axles, Wheel, AxisCarController... -- reglee champ par
    // champ ; il faut savoir lesquels existent et ce que valent ceux de la Satsuma et de la Ruscko). [Test] Autotest=unitycardump,
    // DumpVoitures=<noms separes par des virgules> : a 35 s, pour chaque voiture, chaque objet de sa hierarchie (inactifs compris)
    // et chaque composant : tous ses champs (publics et prives, classes de base comprises jusqu'a MonoBehaviour), les objets non
    // Unity qu'ils contiennent (Axle des Axles, tableaux) un niveau plus bas ; Rigidbody et colliders par leurs proprietes.
    // -> dumps\unitycar-<voiture>.txt. Vient du jeu : reste dans ref\ (hors depot).
    static class UnityCarDump
    {
        public static void Run()
        {
            string list = Config.Get("Test", "DumpVoitures", Game.Msc ? "SATSUMA,RCO_RUSCKO12(270)" : "SORBET(190-200psi),CORRIS");
            foreach (string n0 in list.Split(','))
            {
                string n = n0.Trim();
                if (n.Length == 0) continue;
                GameObject car = FindCar(n);
                if (car == null) { Log.Warn("unitycardump : " + n + " introuvable"); continue; }
                string dir = Path.Combine(Log.DataDir, "dumps");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "unitycar-" + Clean(n) + ".txt");
                var sb = new StringBuilder();
                sb.Append("# ").Append(Recon.Path(car.transform)).Append(" -- ").Append(Game.Name).Append(", ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).Append("\n");
                int nc = 0;
                foreach (Transform t in car.GetComponentsInChildren<Transform>(true)) nc += DumpObject(sb, car.transform, t);
                File.WriteAllText(file, sb.ToString());
                Log.Info("unitycardump : " + n + " -> " + file + " (" + nc + " composants)");
            }
        }

        static GameObject FindCar(string n)
        {
            GameObject g = GameObject.Find(n) ?? Game.FindAny(n);
            if (g != null) return g;
            foreach (Rigidbody rb in UnityEngine.Object.FindObjectsOfType<Rigidbody>())
                if (rb.transform.parent == null && rb.name.StartsWith(n)) return rb.gameObject;
            return null;
        }

        static string Clean(string s) { var b = new StringBuilder(); foreach (char c in s) b.Append(char.IsLetterOrDigit(c) || c == '-' ? c : '_'); return b.ToString(); }

        static int DumpObject(StringBuilder sb, Transform root, Transform t)
        {
            Component[] cs = t.GetComponents<Component>();
            string rel = t == root ? "." : Recon.Path(t).Substring(Recon.Path(root).Length + 1);
            sb.Append("\n== ").Append(rel).Append(t.gameObject.activeSelf ? "" : " [off]").Append("  pos ").Append(V(root.InverseTransformPoint(t.position)))
              .Append(" rot ").Append(V((Quaternion.Inverse(root.rotation) * t.rotation).eulerAngles)).Append(" echelle ").Append(V(t.localScale)).Append("\n");
            int n = 0;
            foreach (Component c in cs)
            {
                if (c == null || c is Transform) continue;
                n++;
                Type ty = c.GetType();
                sb.Append("  [").Append(ty.Name).Append("]");
                Behaviour be = c as Behaviour;
                if (be != null && !be.enabled) sb.Append(" (coupe)");
                if (ty.Name == "PlayMakerFSM") { PropertyInfo fn = ty.GetProperty("FsmName"); sb.Append(" ").Append(fn != null ? fn.GetValue(c, null) : "?").Append("\n"); continue; }
                sb.Append("\n");
                if (c is Rigidbody)
                {
                    var r = (Rigidbody)c;
                    sb.Append("    mass=").Append(F(r.mass)).Append(" drag=").Append(F(r.drag)).Append(" angularDrag=").Append(F(r.angularDrag))
                      .Append(" centerOfMass=").Append(V(r.centerOfMass)).Append(" inertiaTensor=").Append(V(r.inertiaTensor))
                      .Append(" inertiaTensorRotation=").Append(V(r.inertiaTensorRotation.eulerAngles)).Append(" interpolation=").Append(r.interpolation)
                      .Append(" collisionDetection=").Append(r.collisionDetectionMode).Append(" maxAngularVelocity=").Append(F(r.maxAngularVelocity))
                      .Append(" solverIterations=").Append(r.solverIterationCount).Append(" isKinematic=").Append(r.isKinematic).Append(" useGravity=").Append(r.useGravity).Append("\n");
                    continue;
                }
                if (c is Collider)
                {
                    var co = (Collider)c;
                    sb.Append("    isTrigger=").Append(co.isTrigger).Append(" material=").Append(co.sharedMaterial != null ? co.sharedMaterial.name : "-");
                    if (c is BoxCollider) sb.Append(" center=").Append(V(((BoxCollider)c).center)).Append(" size=").Append(V(((BoxCollider)c).size));
                    else if (c is SphereCollider) sb.Append(" center=").Append(V(((SphereCollider)c).center)).Append(" radius=").Append(F(((SphereCollider)c).radius));
                    else if (c is CapsuleCollider) sb.Append(" center=").Append(V(((CapsuleCollider)c).center)).Append(" radius=").Append(F(((CapsuleCollider)c).radius)).Append(" height=").Append(F(((CapsuleCollider)c).height));
                    else if (c is MeshCollider) sb.Append(" mesh=").Append(((MeshCollider)c).sharedMesh != null ? ((MeshCollider)c).sharedMesh.name + " (" + ((MeshCollider)c).sharedMesh.vertexCount + " sommets)" : "-").Append(" convex=").Append(((MeshCollider)c).convex);
                    sb.Append("\n");
                    continue;
                }
                if (c is Renderer || c is MeshFilter || c is AudioSource || c is Light || c is Animation) continue;   // (rien a regler)
                Fields(sb, c, ty, "    ", 0);
            }
            return n;
        }

        static void Fields(StringBuilder sb, object o, Type ty, string ind, int depth)
        {
            for (Type k = ty; k != null && k != typeof(MonoBehaviour) && k != typeof(Behaviour) && k != typeof(Component) && k != typeof(object); k = k.BaseType)
                foreach (FieldInfo f in k.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    object v; try { v = f.GetValue(o); } catch { continue; }
                    sb.Append(ind).Append(f.IsPublic ? "" : "~").Append(f.Name).Append(" : ").Append(f.FieldType.Name).Append(" = ").Append(Val(v)).Append("\n");
                    if (depth < 1 && v != null && !(v is UnityEngine.Object) && !(v is string) && !f.FieldType.IsPrimitive && !f.FieldType.IsEnum && !(v is Vector2) && !(v is Vector3) && !(v is Quaternion) && !(v is Color))
                    {
                        if (v is IEnumerable)
                        {
                            int i = 0;
                            foreach (object e in (IEnumerable)v)
                            {
                                if (i >= 8) { sb.Append(ind).Append("  ...\n"); break; }
                                if (e != null && !(e is UnityEngine.Object) && !e.GetType().IsPrimitive) { sb.Append(ind).Append("  [").Append(i).Append("]\n"); Fields(sb, e, e.GetType(), ind + "    ", depth + 1); }
                                i++;
                            }
                        }
                        else Fields(sb, v, v.GetType(), ind + "  ", depth + 1);
                    }
                }
        }

        static string Val(object v)
        {
            if (v == null) return "null";
            if (v is float) return F((float)v);
            if (v is Vector3) return V((Vector3)v);
            if (v is UnityEngine.Object) { var u = (UnityEngine.Object)v; if (u == null) return "null (detruit)"; var c = u as Component; return c != null ? c.GetType().Name + "@" + Recon.Path(c.transform) : u.GetType().Name + " " + u.name; }
            if (v is float[]) { var a = (float[])v; var b = new StringBuilder("["); for (int i = 0; i < a.Length; i++) b.Append(i > 0 ? ", " : "").Append(F(a[i])); return b.Append("]").ToString(); }
            if (v is AnimationCurve) { var ac = (AnimationCurve)v; var b = new StringBuilder("courbe"); foreach (Keyframe kf in ac.keys) b.Append(" (").Append(F(kf.time)).Append(", ").Append(F(kf.value)).Append(")"); return b.ToString(); }
            if (v is Array) return v.GetType().GetElementType().Name + "[" + ((Array)v).Length + "]";
            return v.ToString();
        }
        static string F(float f) { return f.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture); }
        static string V(Vector3 v) { return "(" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + ")"; }
    }
}
