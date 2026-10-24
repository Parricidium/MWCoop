using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Voyage
{
    // Essais sans clavier (instances de test) : variable d'environnement VOYAGE_ESSAI, commandes separees par ';', jouees
    // dans l'ordre une fois la partie chargee. Photos et releves dans <jeu>\Mods\voyage-essai\.
    //   attendre s | tp x y z [cap] | argent n | photo nom x y z cx cy cz | dessus nom x z demi-cote
    //   sonde x z demi-cote pas | alentours x z rayon | acheter | embarquer | rentrer | etat | quitter
    static class Essai
    {
        static readonly Queue<string> todo = new Queue<string>();
        static float waitUntil;
        static bool started;
        static readonly CultureInfo ci = CultureInfo.InvariantCulture;

        public static bool Active { get { return started; } }
        public static Ray? AimRay;
        static float msgWaitFrom = -1f;
        static bool quiet;
        // remet la commande en tete de file (attente sans bloquer le jeu)
        static void Requeue(string c) { var rest = todo.ToArray(); todo.Clear(); todo.Enqueue(c); foreach (string r in rest) todo.Enqueue(r); waitUntil = Time.realtimeSinceStartup + 0.5f; }
        public static bool Click;

        public static void Start()
        {
            string s = Environment.GetEnvironmentVariable("VOYAGE_ESSAI");
            if (string.IsNullOrEmpty(s)) return;
            foreach (string c in s.Split(';')) if (c.Trim().Length > 0) todo.Enqueue(c.Trim());
            started = true;
            waitUntil = Time.realtimeSinceStartup + 3f;
            Journal.Info("essai : " + todo.Count + " commandes");
        }

        static string Dir { get { string d = Path.Combine(Path.GetDirectoryName(Application.dataPath), Path.Combine("Mods", "voyage-essai")); Directory.CreateDirectory(d); return d; } }
        static float F(string s) { return float.Parse(s, ci); }

        public static void Tick()
        {
            if (!started || todo.Count == 0 || Time.realtimeSinceStartup < waitUntil) return;
            string c = todo.Dequeue();
            string[] a = c.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (!quiet) Journal.Info("essai > " + c);
            quiet = false;
            try
            {
                switch (a[0])
                {
                    case "attendre": waitUntil = Time.realtimeSinceStartup + F(a[1]); break;
                    case "tp": Jeu.Teleport(new Vector3(F(a[1]), F(a[2]), F(a[3])), a.Length > 4 ? F(a[4]) : 0f); waitUntil = Time.realtimeSinceStartup + 1f; break;
                    case "argent": if (Jeu.Money != null) Jeu.Money.Value = F(a[1]); break;
                    case "photo": Photo(a[1], new Vector3(F(a[2]), F(a[3]), F(a[4])), new Vector3(F(a[5]), F(a[6]), F(a[7])), false, 0f); break;
                    case "photosol":   // photosol nom x z h cx cz ch : hauteurs au-dessus du sol (celui d'un circuit compris)
                        Photo(a[1], new Vector3(F(a[2]), Lieux.GroundY(F(a[2]), F(a[3])) + F(a[4]), F(a[3])), new Vector3(F(a[5]), Lieux.GroundY(F(a[5]), F(a[6])) + F(a[7]), F(a[6])), false, 0f);
                        break;
                    case "dessus": Photo(a[1], new Vector3(F(a[2]), 400f, F(a[3])), new Vector3(F(a[2]), 0f, F(a[3]) + 0.001f), true, F(a[4])); break;
                    case "sonde": Probe(F(a[1]), F(a[2]), F(a[3]), F(a[4])); break;
                    case "alentours": Around(F(a[1]), F(a[2]), F(a[3])); break;
                    case "chercher": Find(a[1]); break;
                    case "photojoueur":   // photojoueur nom [cap en plus] : ce que voit le joueur, a hauteur d'yeux
                    {
                        Transform pl = Jeu.Player; float yaw = pl.eulerAngles.y + (a.Length > 2 ? F(a[2]) : 0f);
                        Vector3 fw = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                        Photo(a[1], pl.position + Vector3.up * 0.7f, pl.position + Vector3.up * 0.5f + fw * 10f, false, 0f);
                        break;
                    }
                    case "destinations": foreach (Destination d in Destinations.All) Journal.Info("destination " + d.Id + " : " + d.Label + ", " + d.Price + " mk, case " + d.Origin + ", arrivee " + d.Arrival); break;
                    case "objet":   // objet <debut du nom> : maillage, matiere, shader, texture
                        foreach (MeshRenderer mr in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
                        {
                            if (!mr.name.StartsWith(a[1])) continue;
                            var mf = mr.GetComponent<MeshFilter>(); Material m = mr.sharedMaterial;
                            Journal.Info(string.Format(ci, "objet {0} : actif {1}, sommets {2}, boite {3}, matiere {4}, shader {5}, texture {6}", mr.name, mr.gameObject.activeInHierarchy,
                                mf != null && mf.sharedMesh != null ? mf.sharedMesh.vertexCount : -1, mr.bounds, m != null ? m.name : "-", m != null && m.shader != null ? m.shader.name : "-",
                                m != null && m.mainTexture != null ? m.mainTexture.name + " " + m.mainTexture.width + "x" + m.mainTexture.height : "-"));
                        }
                        break;
                    case "profilroutes":   // (Mods\profil-routes.txt : une route par ligne, points x,z tous les 50 cm) marches du sol
                    {
                        string f = Path.Combine(Path.GetDirectoryName(Application.dataPath), Path.Combine("Mods", "profil-routes.txt"));
                        int routes = 0, steps5 = 0, steps10 = 0, n = 0; float worst = 0f; Vector3 worstAt = Vector3.zero;
                        var marches = new List<KeyValuePair<float, string>>(); string prevW = "";
                        foreach (string line in File.ReadAllLines(f))
                        {
                            float prev = float.NaN; routes++;
                            foreach (string pt in line.Split(';'))
                            {
                                string[] xy = pt.Split(',');
                                if (xy.Length < 2) continue;
                                float x = F(xy[0]), z = F(xy[1]), y; string w;
                                if (!Ground(x, z, out y, out w)) { prev = float.NaN; continue; }
                                n++;
                                if (!float.IsNaN(prev))
                                {
                                    float d = Mathf.Abs(y - prev);
                                    if (d > 0.05f) steps5++;
                                    if (d > 0.10f) { steps10++; marches.Add(new KeyValuePair<float, string>(d, string.Format(ci, "{0,6:F2} m en {1:F1},{2:F1},{3:F1}  {4} -> {5}", d, x, y, z, prevW, w))); }
                                    if (d > worst) { worst = d; worstAt = new Vector3(x, y, z); }
                                }
                                prev = y; prevW = w;
                            }
                        }
                        marches.Sort((u, v) => v.Key.CompareTo(u.Key));
                        File.WriteAllText(Path.Combine(Dir, "marches.txt"), string.Join(Environment.NewLine, marches.Select(m => m.Value).ToArray()));
                        Journal.Info(string.Format(ci, "essai : profil de {0} routes, {1} points : marches > 5 cm {2}, > 10 cm {3}, pire {4:F2} m en {5}", routes, n, steps5, steps10, worst, worstAt));
                        break;
                    }
                    case "attendremessage":   // (co-op : jusqu'au prochain message d'un autre joueur, 150 s au plus)
                        if (msgWaitFrom < 0f) msgWaitFrom = Time.realtimeSinceStartup;
                        if (Coop.LastMessageAt < 0f && Time.realtimeSinceStartup - msgWaitFrom < 150f) { Requeue(c); quiet = true; }
                        else { Journal.Info("essai : message recu (ou delai)"); Coop.LastMessageAt = -1f; msgWaitFrom = -1f; }
                        break;
                    case "photocine":   // ce que montre la camera de cinema du vol
                        if (Cine.Cam != null) { Shot(a[1], Cine.Cam); } else Journal.Info("essai : pas de camera de cinema");
                        break;
                    case "sons":
                    {
                        var names = new SortedDictionary<string, string>();
                        foreach (AudioClip ac in Resources.FindObjectsOfTypeAll<AudioClip>())
                            if (ac != null && !names.ContainsKey(ac.name)) names[ac.name] = ac.length.ToString("F1", ci) + " s";
                        var users = new List<string>();
                        foreach (AudioSource s in Resources.FindObjectsOfTypeAll<AudioSource>())
                            if (s != null && s.clip != null && s.gameObject.hideFlags == HideFlags.None) users.Add(s.clip.name + "  <-  " + PathOf(s.transform));
                        users.Sort(string.CompareOrdinal);
                        File.WriteAllText(Path.Combine(Dir, "sons.txt"), string.Join(Environment.NewLine, names.Select(k => k.Key + "  " + k.Value).ToArray()) + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, users.ToArray()));
                        Journal.Info("essai : " + names.Count + " sons");
                        break;
                    }
                    // visee simulee (de x y z vers x y z), clic simule a la prochaine image, texte de la main
                    case "viser": AimRay = new Ray(new Vector3(F(a[1]), F(a[2]), F(a[3])), new Vector3(F(a[4]), F(a[5]), F(a[6])) - new Vector3(F(a[1]), F(a[2]), F(a[3]))); waitUntil = Time.realtimeSinceStartup + 0.3f; break;
                    case "nepasviser": AimRay = null; break;
                    // sauvegarde en jeu par MWCoop (Game.SaveInPlace : l'evenement SAVEGAME du jeu, que MSCLoader suit -> OnSave)
                    case "sauver":
                    {
                        Type g = Type.GetType("MWCoop.Game, MWCoop");
                        var m = g != null ? g.GetMethod("SaveInPlace", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static) : null;
                        if (m != null) m.Invoke(null, null); else Journal.Info("essai : sauvegarde en jeu indisponible (MWCoop)");
                        waitUntil = Time.realtimeSinceStartup + 6f;
                        break;
                    }
                    case "clic": Click = true; waitUntil = Time.realtimeSinceStartup + 0.6f; break;
                    case "main": Journal.Info("main : « " + (Jeu.GuiText != null ? Jeu.GuiText.Value : "?") + " » utiliser " + (Jeu.GuiUse != null && Jeu.GuiUse.Value) + " acheter " + (Jeu.GuiBuy != null && Jeu.GuiBuy.Value) + ", sous-titre « " + (Jeu.GuiSub != null ? Jeu.GuiSub.Value : "?") + " »"); break;
                    case "matieres":
                    {
                        var names = new SortedDictionary<string, string>();
                        foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
                            if (m != null && !names.ContainsKey(m.name)) names[m.name] = (m.shader != null ? m.shader.name : "?") + "  " + (m.mainTexture != null ? m.mainTexture.name : "-");
                        File.WriteAllText(Path.Combine(Dir, "matieres.txt"), string.Join(Environment.NewLine, names.Select(k => k.Key + "  |  " + k.Value).ToArray()));
                        Journal.Info("essai : " + names.Count + " matieres");
                        break;
                    }
                    case "acheter": Voyage.Instance.BuyTicket(a.Length > 1 ? Destinations.Find(a[1]) : (Destinations.All.Count > 0 ? Destinations.All[0] : null)); break;
                    case "embarquer": Voyage.Instance.Board(); waitUntil = Time.realtimeSinceStartup + 1f; break;
                    case "rentrer": Voyage.Instance.ComeBack(); waitUntil = Time.realtimeSinceStartup + 1f; break;
                    case "etat": Journal.Info("etat : " + Voyage.Instance.State() + ", joueur " + (Jeu.Player != null ? Jeu.Player.position.ToString("F1") : "?") + ", argent " + (Jeu.Money != null ? Jeu.Money.Value.ToString("F0") : "?")); break;
                    case "quitter": Journal.Info("essai : fin"); Application.Quit(); break;
                    default: Journal.Info("essai : commande inconnue " + a[0]); break;
                }
            }
            catch (Exception e) { Journal.Info("essai : " + c + " -> " + e); }
        }

        // photo d'une camera a part (lumiere de jour ajoutee : une nouvelle partie commence la nuit) ; ortho = vue de dessus
        // photo d'une camera existante (cinema du vol), lumiere de jour ajoutee comme pour Photo
        static void Shot(string name, Camera cam)
        {
            var lg = new GameObject("voyage photo (lumiere)");
            var li = lg.AddComponent<Light>(); li.type = LightType.Directional; li.intensity = 1.1f; lg.transform.rotation = Quaternion.Euler(50, 30, 0);
            Color amb = RenderSettings.ambientLight; RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.62f);
            var rt = new RenderTexture(1280, 720, 24);
            RenderTexture keep = cam.targetTexture;
            cam.targetTexture = rt; cam.Render(); cam.targetTexture = keep;
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(Dir, name + ".png"), tex.EncodeToPNG());
            RenderSettings.ambientLight = amb;
            UnityEngine.Object.Destroy(lg); UnityEngine.Object.Destroy(rt); UnityEngine.Object.Destroy(tex);
            Journal.Info("essai : photo " + name + " (cinema, avion en " + (Avion.T != null ? Avion.T.position.ToString("F1") : "?") + ")");
        }

        static void Photo(string name, Vector3 from, Vector3 at, bool ortho, float half)
        {
            var go = new GameObject("voyage photo");
            var cam = go.AddComponent<Camera>();
            var lg = new GameObject("voyage photo (lumiere)");
            var li = lg.AddComponent<Light>(); li.type = LightType.Directional; li.intensity = 1.1f; lg.transform.rotation = Quaternion.Euler(50, 30, 0);
            Color amb = RenderSettings.ambientLight; RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.62f);
            bool fog = RenderSettings.fog; RenderSettings.fog = false;
            go.transform.position = from;
            go.transform.LookAt(at);
            cam.fieldOfView = 60; cam.farClipPlane = 3000; cam.nearClipPlane = 0.1f;
            if (ortho) { cam.orthographic = true; cam.orthographicSize = half; }
            int w = 1280, h = ortho ? 1280 : 720;
            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt; cam.Render();
            Construction.TickTexts(true); cam.Render();   // (2e rendu : l'atlas des lettres refait pendant le 1er)
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = null; cam.targetTexture = null;
            File.WriteAllBytes(Path.Combine(Dir, name + ".png"), tex.EncodeToPNG());
            RenderSettings.ambientLight = amb; RenderSettings.fog = fog;
            UnityEngine.Object.Destroy(lg); UnityEngine.Object.Destroy(go); UnityEngine.Object.Destroy(rt); UnityEngine.Object.Destroy(tex);
            Journal.Info("essai : photo " + name);
        }

        // hauteur du sol (premier obstacle solide sous le ciel) et ce qui a ete touche
        public static bool Ground(float x, float z, out float y, out string what)
        {
            y = 0f; what = "";
            RaycastHit[] hs = Physics.RaycastAll(new Vector3(x, 600f, z), Vector3.down, 1200f);
            float best = float.MaxValue;
            foreach (RaycastHit h in hs)
            {
                if (h.collider.isTrigger) continue;
                if (h.distance < best) { best = h.distance; y = h.point.y; what = h.collider.transform.root.name + "/" + h.collider.name; }
            }
            return best < float.MaxValue;
        }

        // grille de hauteurs (centre x, z ; demi-cote ; pas) : lignes nord -> sud, et ce qui depasse du terrain
        static void Probe(float cx, float cz, float half, float step)
        {
            var sb = new StringBuilder();
            var other = new Dictionary<string, int>();
            for (float z = cz + half; z >= cz - half - 0.01f; z -= step)
            {
                sb.Append(z.ToString("F0", ci).PadLeft(6)).Append(" |");
                for (float x = cx - half; x <= cx + half + 0.01f; x += step)
                {
                    float y; string w;
                    if (!Ground(x, z, out y, out w)) { sb.Append("     ."); continue; }
                    string root = w.Split('/')[0];
                    bool terrain = w.IndexOf("TERRAIN", StringComparison.OrdinalIgnoreCase) >= 0 || w.IndexOf("Grass", StringComparison.OrdinalIgnoreCase) >= 0 || w.IndexOf("Field", StringComparison.OrdinalIgnoreCase) >= 0;
                    sb.Append(y.ToString("F1", ci).PadLeft(6)).Append(terrain ? " " : "*");
                    if (!terrain) { int n; other.TryGetValue(w, out n); other[w] = n + 1; }
                }
                sb.Append("\r\n");
            }
            File.WriteAllText(Path.Combine(Dir, string.Format(ci, "sonde_{0:F0}_{1:F0}.txt", cx, cz)), sb.ToString() + "\r\n* = pas le sol :\r\n" + string.Join("\r\n", other.OrderByDescending(k => k.Value).Select(k => k.Value + "  " + k.Key).ToArray()));
            Journal.Info(string.Format(ci, "essai : sonde {0:F0},{1:F0} ecrite ({2} objets hors sol)", cx, cz, other.Count));
        }

        // ce qui vit pres d'un point : declencheurs (objectifs, zones de boulots), automates, objets nommes
        static void Around(float cx, float cz, float r)
        {
            var c = new Vector3(cx, 0f, cz);
            var lines = new List<string>();
            foreach (Collider col in Resources.FindObjectsOfTypeAll<Collider>())
            {
                if (col == null || !col.gameObject.activeInHierarchy && !col.isTrigger) continue;
                if (col.gameObject.hideFlags != HideFlags.None) continue;
                Vector3 p = col.bounds.center; p.y = 0f;
                if ((p - c).magnitude > r) continue;
                if (!col.isTrigger) continue;
                lines.Add(string.Format(ci, "declencheur {0,7:F1} m  {1}{2}  @ {3:F0},{4:F0}", (p - c).magnitude, PathOf(col.transform), col.gameObject.activeInHierarchy ? "" : " (inactif)", p.x, p.z));
            }
            foreach (PlayMakerFSM f in Resources.FindObjectsOfTypeAll<PlayMakerFSM>())
            {
                if (f == null || f.gameObject.hideFlags != HideFlags.None) continue;
                Vector3 p = f.transform.position; p.y = 0f;
                if ((p - c).magnitude > r) continue;
                lines.Add(string.Format(ci, "automate    {0,7:F1} m  {1} [{2}]{3}  @ {4:F0},{5:F0}", (p - c).magnitude, PathOf(f.transform), f.FsmName, f.gameObject.activeInHierarchy ? "" : " (inactif)", p.x, p.z));
            }
            lines.Sort((x, y) => string.CompareOrdinal(x.Substring(12), y.Substring(12)));
            File.WriteAllText(Path.Combine(Dir, string.Format(ci, "alentours_{0:F0}_{1:F0}.txt", cx, cz)), string.Join("\r\n", lines.Distinct().ToArray()));
            Journal.Info(string.Format(ci, "essai : alentours {0:F0},{1:F0} : {2} lignes", cx, cz, lines.Count));
        }

        // objets dont le nom contient un motif (positions) : evenements, courses, boulots
        static void Find(string motif)
        {
            var lines = new List<string>();
            foreach (Transform t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (t == null || t.gameObject.hideFlags != HideFlags.None || t.name.IndexOf(motif, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (t.parent != null && t.parent.name.IndexOf(motif, StringComparison.OrdinalIgnoreCase) >= 0) continue;
                lines.Add(string.Format(ci, "{0,8:F0} {1,6:F1} {2,8:F0}  {3}{4}", t.position.x, t.position.y, t.position.z, PathOf(t), t.gameObject.activeInHierarchy ? "" : " (inactif)"));
            }
            lines.Sort(string.CompareOrdinal);
            File.WriteAllText(Path.Combine(Dir, "chercher_" + motif + ".txt"), string.Join(Environment.NewLine, lines.ToArray()));
            Journal.Info("essai : chercher " + motif + " : " + lines.Count);
        }

        static string PathOf(Transform t) { string s = t.name; for (Transform p = t.parent; p != null; p = p.parent) s = p.name + "/" + s; return s; }
    }
}
