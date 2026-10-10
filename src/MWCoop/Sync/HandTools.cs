using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Outils en main (demande de JD, 10/10 : « trouve tous les objets tenables en main -- hache, marteau, pistolet de la
    // pompe a essence... -- et affiche-les »). Le jeu ne les tient pas comme des objets : l'objet ramasse (ax(itemx),
    // sledgehammer(itemx), flashlight(itemx)...) est eteint (Hand :: PickUp, « Check item » -> Ax, Flashlight...) et un
    // modele a la premiere personne s'allume sous la camera du joueur. Chez les autres, rien (l'objet eteint en main est
    // meme cache par Props). Ici :
    //  - joueur local : l'outil allume (table ci-dessous, chemins sous PLAYER/Pivot/AnimPivot/Camera), la lampe allumee
    //    et l'animation en cours (coup de masse...) partent a chaque changement et toutes les 5 s (@outil) ;
    //  - chez les autres : copie du meme modele (pris sous le PLAYER d'ici : memes maillages chez tous), sans ses
    //    automates ni sa physique, posee devant la tete de son avatar comme devant sa camera ; la main droite (et la
    //    gauche pour les outils longs) prend le manche (Avatar.ToolGrip) ; l'animation est rejouee, la lampe eclaire.
    public static class HandTools
    {
        // Cle envoyee (rang + 1 dans la table), chemin du modele a copier sous Camera (« ** » : cherche par nom dessous).
        static readonly string[][] Table = {
            new[] { "hache", "Ax/Pivot" },
            new[] { "masse", "FPSCamera/FPSCamera/Sledgehammer/Pivot" },
            new[] { "barre a mine", "FPSCamera/FPSCamera/DiggingBar" },
            new[] { "bombe de peinture", "FPSCamera/SprayCan" },
            new[] { "lampe", "FPSCamera/Flashlight" },
            new[] { "extincteur", "FPSCamera/Extinguisher" },
            new[] { "rouleau de tissu", "FPSCamera/FabricRoll" },
            new[] { "grattoir", "FPSCamera/IceScraper" },
            new[] { "louche du sauna", "FPSCamera/FPSCamera/SaunaThrow" },
            new[] { "pistolet 98", "FPSCamera/FPSCamera/Fuel/Pivot/**/Pistol 98" },
            new[] { "pistolet diesel", "FPSCamera/FPSCamera/Fuel/Pivot/**/Pistol D" },
            new[] { "pistolet fioul", "FPSCamera/FPSCamera/Fuel/Pivot/**/Pistol PÖ" },
            new[] { "metre", "FPSCamera/2Spanner/Pivot/Ruler" },
            new[] { "cliquet", "FPSCamera/2Spanner/Pivot/Ratchet" },
            new[] { "cle", "FPSCamera/2Spanner/Pivot/Spanner" },
            new[] { "tournevis", "FPSCamera/2Spanner/Pivot/Screwdriver" },
            new[] { "cle a bougie", "FPSCamera/2Spanner/Pivot/SparkplugWrench" },
            new[] { "cle a roue", "FPSCamera/2Spanner/Pivot/LugWrench" },
        };
        public const int F_Light = 1, F_Anim = 2;
        // Main droite tenant l'outil, dans le repere du regard (m : droite, haut, avant). [Test] OutilMain=x,y,z
        static Vector3 HandAt
        {
            get
            {
                string[] c = Config.Get("Test", "OutilMain", "0.2,-0.45,0.4").Split(',');
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                float x, y, z;
                if (c.Length == 3 && float.TryParse(c[0], System.Globalization.NumberStyles.Float, ci, out x) && float.TryParse(c[1], System.Globalization.NumberStyles.Float, ci, out y) && float.TryParse(c[2], System.Globalization.NumberStyles.Float, ci, out z)) return new Vector3(x, y, z);
                return new Vector3(0.2f, -0.45f, 0.4f);
            }
        }

        static Transform camBase, cam;
        static Transform[] srcs;
        static float nextFind, nextSend;
        static int sentTool = -1, sentFlags = -1, tool, flags;

        class Remote
        {
            public int Tool, Flags;
            public GameObject Holder, Copy;
            public Vector3 RelPos; public Quaternion RelRot;   // support de la copie dans le repere de la camera
            public Transform GripT; public Vector3 GripA, GripB; public bool Two;   // prises (repere du maillage principal)
            public Animation Anim; public Light Lamp; public bool AnimWas;
        }
        static readonly Dictionary<int, Remote> remote = new Dictionary<int, Remote>();

        public static void OnLevelLoaded()
        {
            camBase = cam = null; srcs = null; testTool = 0; testCapped = 0; nextFind = 0; sentTool = sentFlags = -1; tool = flags = 0;
            foreach (Remote r in remote.Values) Clear(r);
            remote.Clear();
        }

        public static void PlayerLeft(int id)
        {
            Remote r;
            if (remote.TryGetValue(id, out r)) { Clear(r); remote.Remove(id); }
        }

        static void Clear(Remote r)
        {
            if (r.Holder != null) Object.Destroy(r.Holder);
            r.Holder = r.Copy = null; r.Anim = null; r.Lamp = null; r.GripT = null;
        }

        static Transform Resolve(Transform root, string path)
        {
            int star = path.IndexOf("/**/");
            if (star < 0) return root.Find(path);
            Transform b = root.Find(path.Substring(0, star));
            return b != null ? FindDeep(b, path.Substring(star + 4)) : null;
        }
        static Transform FindDeep(Transform t, string name)
        {
            foreach (Transform c in t)
            {
                if (c.name == name) return c;
                Transform f = FindDeep(c, name);
                if (f != null) return f;
            }
            return null;
        }

        static bool Find()
        {
            if (srcs != null && camBase != null) return true;
            if (Time.realtimeSinceStartup < nextFind) return false;
            nextFind = Time.realtimeSinceStartup + 3f;
            GameObject cb = Game.PlayerPart("Pivot/AnimPivot/Camera");
            if (cb == null) return false;
            camBase = cb.transform;
            Transform outer = camBase.Find("FPSCamera");
            cam = outer != null ? (outer.Find("FPSCamera") ?? outer) : camBase;
            srcs = new Transform[Table.Length];
            var missing = new List<string>();
            for (int i = 0; i < Table.Length; i++) { srcs[i] = Resolve(camBase, Table[i][1]); if (srcs[i] == null) missing.Add(Table[i][0]); }
            Log.Info("outils en main : " + (Table.Length - missing.Count) + "/" + Table.Length + " trouves" + (missing.Count > 0 ? " (absents : " + string.Join(", ", missing.ToArray()) + ")" : ""));
            return true;
        }

        // [Test] OutilForce=n : outil n (1..) montre comme tenu (essais, sans le prendre en main).
        public static void Update()
        {
            if (!Session.Active || !PlayerSync.InGame || !Find()) return;
            int t = 0, fl = 0;
            int forced = testTool > 0 ? testTool : Config.GetInt("Test", "OutilForce", 0);
            if (forced > 0 && forced <= Table.Length) t = forced;
            else
                for (int i = 0; i < srcs.Length; i++)
                    if (srcs[i] != null && srcs[i].gameObject.activeInHierarchy && HasMesh(srcs[i])) { t = i + 1; break; }
            if (t > 0 && srcs[t - 1] != null)
            {
                Transform s = srcs[t - 1];
                foreach (Light l in s.GetComponentsInChildren<Light>(false)) if (l.enabled && l.intensity > 0.01f) fl |= F_Light;
                for (Transform a = s; a != null && a != camBase; a = a.parent)
                {
                    Animation an = a.GetComponent<Animation>();
                    if (an != null && an.isPlaying) { fl |= F_Anim; break; }
                }
            }
            tool = t; flags = fl;
            if (ThirdPerson.Active) Local(t, fl); else if (remote.ContainsKey(Session.LocalId)) PlayerLeft(Session.LocalId);
            float now = Time.realtimeSinceStartup;
            if (t != sentTool || fl != sentFlags || now >= nextSend)
            {
                if (t != sentTool) Log.Info("outil en main : " + (t > 0 ? Table[t - 1][0] : "aucun"));
                bool changed = t != sentTool || fl != sentFlags;
                sentTool = t; sentFlags = fl; nextSend = now + 5f;
                Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@outil").U8(t).U8(fl), changed);
            }
        }

        static bool HasMesh(Transform t)
        {
            foreach (MeshRenderer m in t.GetComponentsInChildren<MeshRenderer>(false)) if (m.enabled) return true;
            return false;
        }

        public static void OnRemote(int who, int t, int fl)
        {
            if (who == Session.LocalId) return;
            Local(who, t, fl);
        }
        // (troisieme personne : l'outil du joueur local sur son propre avatar)
        static void Local(int t, int fl) { Local(Session.LocalId, t, fl); }
        static void Local(int who, int t, int fl)
        {
            Remote r;
            if (!remote.TryGetValue(who, out r)) { r = new Remote(); remote[who] = r; }
            if (t != Mathf.Abs(r.Tool)) { Clear(r); r.Tool = t; Log.Info("outil en main de #" + who + " : " + (t > 0 && t <= Table.Length ? Table[t - 1][0] : "aucun")); }
            r.Flags = fl;
        }

        // Copie du modele d'ici : support au repere du parent de la source (taille comprise), copie dedans a sa place
        // locale (l'animation la deplace par rapport au support, comme chez le joueur) ; automates, physique, sons,
        // particules et mains a la premiere personne retires ; calque de l'avatar (les outils sont sur le calque de la
        // camera des mains, invisible a la camera du monde).
        static bool Build(Remote r, Avatar a)
        {
            if (!Find() || r.Tool <= 0 || r.Tool > srcs.Length || srcs[r.Tool - 1] == null || cam == null) return false;
            Transform s = srcs[r.Tool - 1];
            Transform sp = s.parent;
            var holder = new GameObject("MWCoop-Outil " + Table[r.Tool - 1][0]);
            holder.transform.localScale = sp != null ? sp.lossyScale : Vector3.one;
            Quaternion inv = Quaternion.Inverse(cam.rotation);
            r.RelPos = inv * ((sp != null ? sp.position : s.position) - cam.position);
            r.RelRot = inv * (sp != null ? sp.rotation : s.rotation);
            // (copie eteinte des sa creation : ses automates ne demarrent pas. La source est eteinte par son parent ;
            // l'eteindre elle-meme ne declenche rien. Tenue par le joueur d'ici en ce moment : plus tard.)
            if (s.gameObject.activeInHierarchy) return false;
            bool was = s.gameObject.activeSelf;
            s.gameObject.SetActive(false);
            GameObject c = (GameObject)Object.Instantiate(s.gameObject);
            s.gameObject.SetActive(was);
            c.transform.SetParent(holder.transform, false);
            c.transform.localPosition = s.localPosition; c.transform.localRotation = s.localRotation; c.transform.localScale = s.localScale;
            Strip(c.transform);
            int layer = a.Root.layer;
            foreach (Transform x in c.GetComponentsInChildren<Transform>(true)) x.gameObject.layer = layer;
            c.SetActive(true);
            r.Holder = holder; r.Copy = c;
            Animation[] ans = c.GetComponentsInChildren<Animation>(true);
            Light[] ls0 = c.GetComponentsInChildren<Light>(true);
            r.Anim = c.GetComponent<Animation>() ?? (ans.Length > 0 ? ans[0] : null);
            r.Lamp = ls0.Length > 0 ? ls0[0] : null;
            if (r.Lamp != null) { r.Lamp.enabled = false; r.Lamp.shadows = LightShadows.None; }
            // Prises : axe le plus long du plus gros maillage ; outil long (> 25 cm) : le bout le plus proche de la camera
            // (le manche), un peu vers l'interieur ; la main gauche plus loin sur le manche s'il depasse 55 cm.
            MeshFilter best = null; float bestV = 0f;
            foreach (MeshFilter mf in c.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Vector3 sz = Vector3.Scale(mf.sharedMesh.bounds.size, mf.transform.lossyScale);
                float v = sz.x * sz.y * sz.z;
                if (best == null || v > bestV) { best = mf; bestV = v; }
            }
            if (best != null)
            {
                Bounds b = best.sharedMesh.bounds;
                Vector3 ls = best.transform.lossyScale;
                Vector3 sz = Vector3.Scale(b.size, ls);
                int ax = sz.x >= sz.y && sz.x >= sz.z ? 0 : sz.y >= sz.z ? 1 : 2;
                float len = sz[ax];
                Vector3 e0 = b.center, e1 = b.center; e0[ax] = b.min[ax]; e1[ax] = b.max[ax];
                // (bout le plus proche de la camera : support pose ici a l'origine, a la meme place qu'a la source)
                holder.transform.position = cam.position + cam.rotation * r.RelPos; holder.transform.rotation = cam.rotation * r.RelRot;
                Vector3 w0 = best.transform.TransformPoint(e0), w1 = best.transform.TransformPoint(e1);
                bool near0 = (w0 - cam.position).sqrMagnitude < (w1 - cam.position).sqrMagnitude;
                Vector3 nearE = near0 ? e0 : e1, farE = near0 ? e1 : e0;
                r.GripT = best.transform;
                if (len > 0.25f) { r.GripA = Vector3.Lerp(nearE, farE, 0.12f); r.Two = len > 0.55f; r.GripB = Vector3.Lerp(nearE, farE, 0.42f); }
                else { r.GripA = b.center; r.Two = false; }
                // Prise ramenee a la place d'une main devant le ventre (repere du regard), l'outil garde son sens : a la
                // premiere personne les petits outils sont contre la camera (cle, bombe : contre le visage de l'avatar).
                Vector3 gripCam = Quaternion.Inverse(cam.rotation) * (best.transform.TransformPoint(r.GripA) - cam.position);
                r.RelPos += HandAt - gripCam;
                Log.Info("outil en main : copie " + Table[r.Tool - 1][0] + ", " + best.name + " " + (len * 100f).ToString("F0") + " cm" + (r.Two ? ", deux mains" : "") + (r.Anim != null ? ", anime" : "") + (r.Lamp != null ? ", lampe" : ""));
            }
            return true;
        }

        static void Strip(Transform root)
        {
            // (ordre : articulations avant les corps ; puis le reste. Les mains a la premiere personne -- maillages
            // a os -- sont eteintes.)
            foreach (Joint j in root.GetComponentsInChildren<Joint>(true)) Object.DestroyImmediate(j);
            foreach (Component k in root.GetComponentsInChildren<Component>(true))
            {
                if (k == null || k is Transform || k is MeshFilter || k is MeshRenderer || k is Animation || k is Light) continue;
                if (k is SkinnedMeshRenderer) { k.gameObject.SetActive(false); continue; }
                if (k is Rigidbody) continue;
                Object.DestroyImmediate(k);
            }
            foreach (Rigidbody rb in root.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
        }

        // Avant les poses des avatars (PlayerSync.LateUpdate) : copie devant la tete affichee, prises des mains.
        public static void LateUpdate()
        {
            if (remote.Count == 0) return;
            var list = new List<Avatar>(PlayerSync.Avatars);
            if (ThirdPerson.Me != null) list.Add(ThirdPerson.Me);
            foreach (Avatar a in list)
            {
                if (a == null || a.Root == null || a.Player == null) continue;
                Remote r;
                bool want = remote.TryGetValue(a.Player.Id, out r) && r.Tool > 0 && a.Root.activeInHierarchy && !a.InVehicle;
                if (!want)
                {
                    a.ToolW = Mathf.MoveTowards(a.ToolW, 0f, Time.deltaTime * 4f);
                    if (r != null && r.Holder != null && r.Holder.activeSelf) r.Holder.SetActive(false);
                    continue;
                }
                if (r.Holder == null && !Build(r, a))
                {
                    if (srcs != null && r.Tool <= srcs.Length && srcs[r.Tool - 1] == null) r.Tool = -r.Tool;   // (absent ici : plus essaye)
                    continue;
                }
                if (!r.Holder.activeSelf) r.Holder.SetActive(true);
                Vector3 hp; Quaternion hr;
                if (!PlayerSync.HeadFrame(a.Player.Id, true, out hp, out hr)) continue;
                r.Holder.transform.position = hp + hr * r.RelPos;
                r.Holder.transform.rotation = hr * r.RelRot;
                bool anim = (r.Flags & F_Anim) != 0;
                if (r.Anim != null && anim && !r.AnimWas && r.Anim.clip != null) r.Anim.Play(r.Anim.clip.name);
                if (r.Anim != null && anim && !r.AnimWas && r.Anim.clip == null) foreach (AnimationState s in r.Anim) { r.Anim.Play(s.name); break; }
                r.AnimWas = anim;
                if (r.Lamp != null) r.Lamp.enabled = (r.Flags & F_Light) != 0;
                a.ToolW = Mathf.MoveTowards(a.ToolW, 1f, Time.deltaTime * 4f);
                if (r.GripT != null)
                {
                    a.ToolGripR = r.GripT.TransformPoint(r.GripA);
                    a.ToolTwo = r.Two;
                    a.ToolGripL = r.GripT.TransformPoint(r.GripB);
                }
            }
        }

        // [Test] Autotest=outils : l'invite montre chaque outil 5 s a partir de 25 s (OutilDebut=n : a partir du n-ieme) ;
        // l'hote (CameraAvatar=...) capture a 4 s de chacun (outil-n) et note l'etat.
        static int testTool;
        public static void Test(string mode, float t)
        {
            if (mode != "outils" || t < 25f) return;
            int first = Config.GetInt("Test", "OutilDebut", 1);
            int i = first + (int)((t - 25f) / 5f);
            if (i > Table.Length) { if (testTool != 0) { testTool = 0; } return; }
            if (!Session.IsHost) { testTool = i; return; }
            // (hote : 3,5 s apres chaque changement d'outil recu, capture nommee d'apres l'outil recu)
            foreach (KeyValuePair<int, Remote> kv in remote)
            {
                int rt = Mathf.Abs(kv.Value.Tool);
                if (rt != testSeen) { testSeen = rt; testSeenAt = t; testCapped = 0; }
                if (rt > 0 && testCapped != rt && t - testSeenAt > 3.5f)
                {
                    testCapped = rt;
                    foreach (Avatar a in PlayerSync.Avatars)
                        if (a != null && a.Player != null && a.Player.Id == kv.Key) Log.Info("autotest : outil " + rt + " chez #" + a.Player.Id + " : " + State(a.Player.Id) + ", prise " + a.ToolW.ToString("F1") + (a.HandRight != null ? ", main droite a " + (a.HandRight.position - a.ToolGripR).magnitude.ToString("F2") + " m de la prise" : ""));
                    Autotest.CaptureSoon("outil-" + rt, 0.05f);
                }
                break;
            }
        }
        static int testCapped, testSeen; static float testSeenAt;

        public static string State(int id)
        {
            Remote r;
            if (!remote.TryGetValue(id, out r) || r.Tool == 0) return "aucun";
            int t = Mathf.Abs(r.Tool);
            return (t <= Table.Length ? Table[t - 1][0] : "?") + (r.Tool < 0 ? " (absent ici)" : r.Holder == null ? " (pas encore copie)" : "") + ((r.Flags & F_Light) != 0 ? ", allumee" : "") + ((r.Flags & F_Anim) != 0 ? ", animee" : "");
        }
    }
}
