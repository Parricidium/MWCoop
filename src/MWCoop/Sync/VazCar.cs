using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;
using UnityEngine;

namespace MWCoop
{
    // Construction d'une VAZ-2108 (Vaz.cs : qui a quelle couleur). Modele « VAZ-2108 Rally » de matthew0451 (Sketchfab,
    // CC BY-NC 4.0), converti et allege hors du jeu (~54 000 triangles, livree retiree) dans MWCoop/vaz : carrosserie,
    // portieres, capot, volant (.p205 : maillages + matieres), textures PNG, infos.txt (centres des roues, charnieres,
    // volant, feux, repere de la voiture : x a droite, y haut, z avant), plaques numero-01..16.png.
    // Meme technique que les Jokkis (Jokkis.Build) : copie de la SORBET (automates de sauvegarde vides, identifiants
    // prefixes, rendus caches, portieres arriere et hayon eteints, degats coupes), essieux deplaces a l'empattement de la VAZ ;
    // puis la VAZ posee pour que le milieu de ses roues coincide avec celui de la SORBET, ses portieres accrochees aux portieres
    // avant de la SORBET (charnieres deplacees sur les siennes : elles s'ouvrent avec les poignees du jeu), roues visibles du
    // rallye du jeu (RALLYCAR3), volant qui suit celui de la SORBET, feux allumes avec les lumieres de la copie, peinture a la
    // couleur du joueur et son numero sur les portieres.
    public static class VazCar
    {
        public static string Dir { get { return Path.Combine(System.Environment.GetEnvironmentVariable("MWCOOP_DIR") ?? "MWCoop", "vaz"); } }
        public static bool HasFiles { get { return File.Exists(Path.Combine(Dir, "carrosserie.p205")); } }

        // Yeux du pilote, repere du modele : siege conducteur a x -0,35, toit interieur vers 1,33, volant en (-0,35 ; 0,86 ; 0,64).
        // DriverHeadPivot est pose 27 cm DERRIERE et 3 cm au-dessus (les yeux sont la, Seats.EyeFromPivot, mesure sur la
        // SORBET) -- retour de JD, 18/10 : pivot pose sur les yeux, conducteur colle au volant, zone d'entree et place du
        // passager trop en avant.
        static readonly Vector3 EyesModel = new Vector3(-0.35f, 1.10f, 0.02f);
        static readonly Vector3 HeadModel = EyesModel - new Vector3(0f, -0.03f, 0.27f);

        // Commandes de la SORBET posees sur les boutons du tableau de bord de la VAZ (centres des pieces du modele, repere
        // du modele ; photo annotee de JD, 18/10) : contact, essuie-glaces et feux sur les trois interrupteurs a gauche des
        // compteurs ; warning et starter dans les rangees de boutons carres de la console ; soufflerie, degivrage sur les
        // interrupteurs du bas ; temperature et direction du chauffage sur les molettes rondes.
        static readonly KeyValuePair<string, Vector3>[] Controls = {
            new KeyValuePair<string, Vector3>("Functions/SteeringColumnPivot/SteeringColumn/IGNITIONxSorbet", new Vector3(-0.206f, 0.830f, 0.790f)),
            new KeyValuePair<string, Vector3>("Functions/Dashboard/Buttons/ButtonWipers", new Vector3(-0.169f, 0.830f, 0.790f)),
            new KeyValuePair<string, Vector3>("Functions/Dashboard/Buttons/ButtonLightModes", new Vector3(-0.132f, 0.830f, 0.790f)),
            new KeyValuePair<string, Vector3>("Functions/Dashboard/Buttons/ButtonHazard", new Vector3(0.026f, 0.854f, 0.787f)),
            new KeyValuePair<string, Vector3>("Functions/Dashboard/Choke/ButtonChoke", new Vector3(0.003f, 0.818f, 0.777f)),
            new KeyValuePair<string, Vector3>("Functions/Dashboard/Buttons/ButtonHeaterBlower", new Vector3(-0.018f, 0.717f, 0.736f)),
            new KeyValuePair<string, Vector3>("Functions/Dashboard/Buttons/ButtonWindowHeater", new Vector3(-0.067f, 0.717f, 0.736f)),
            new KeyValuePair<string, Vector3>("Functions/Dashboard/Buttons/ButtonHeaterTemp", new Vector3(0.002f, 0.751f, 0.761f)),
            new KeyValuePair<string, Vector3>("Functions/Dashboard/Buttons/ButtonHeaterDirection", new Vector3(-0.068f, 0.751f, 0.761f)),
        };
        // Frein a main : levier du modele (frein.p205) pivotant a sa base (sur la console), poignee vers l'avant ; le levier
        // cliquable du jeu (Functions/HandBrake/LeverPivot) pose sur la poignee.
        static readonly Vector3 BrakePivotModel = new Vector3(0.001f, 0.509f, 0.05f), BrakeGripModel = new Vector3(-0.069f, 0.533f, 0.24f);
        // Son du moteur (facultatif, VazSound) : moteur transversal avant.
        static readonly Vector3 EngineModel = new Vector3(0f, 0.60f, 1.50f);

        public class Built
        {
            public GameObject Car;
            public Transform Body, SteerPivot, SorbetSteer, BrakePivot, SorbetBrake;
            public Quaternion SorbetSteerRest, SorbetBrakeRest;
            public VazSound Sound;
            public Vector3 SteerAxis = Vector3.forward;
            public Component Axis;
            public Drivetrain Dt;
            public Rigidbody Rb;
            public readonly List<Renderer> Hidden = new List<Renderer>();
            public readonly List<KeyValuePair<MeshRenderer, string>> Lamps = new List<KeyValuePair<MeshRenderer, string>>();
            public readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>(), Lit = new Dictionary<string, Material>(), Dim = new Dictionary<string, Material>();
            public readonly List<Light> Front = new List<Light>(), Rear = new List<Light>(), Brake = new List<Light>(), Reverse = new List<Light>();
            public bool LampsInit, FrontOn, RearOn, BrakeOn, ReverseOn;
            public float NextLamp, NextHide, NextSpec, NextMask, NextGrip;
            public readonly float[] LastGrip = { -1f, -1f };
            public int MaskLogs;
        }

        // ---------------------------------------------------------------- fichiers
        class MatDef { public string Key, Kind, Tex; public Color Col; }
        class Part { public string Mat; public Mesh Mesh; }
        static readonly Dictionary<string, List<Part>> meshCache = new Dictionary<string, List<Part>>();
        static readonly Dictionary<string, MatDef> matDefs = new Dictionary<string, MatDef>();
        static readonly Dictionary<string, Texture2D> texCache = new Dictionary<string, Texture2D>();
        static Dictionary<string, Vector3> info;

        // Maillages lus une fois (partages par toutes les VAZ : seules les matieres de peinture et de numero sont a chacune).
        static List<Part> Load(string file)
        {
            List<Part> list;
            if (meshCache.TryGetValue(file, out list)) return list;
            list = new List<Part>();
            string path = Path.Combine(Dir, file);
            if (File.Exists(path))
                using (var r = new BinaryReader(File.OpenRead(path), System.Text.Encoding.UTF8))
                {
                    if (new string(r.ReadChars(4)) != "P205") throw new System.Exception("vaz : " + file + " : format inconnu");
                    r.ReadInt32();
                    int nm = r.ReadInt32();
                    for (int i = 0; i < nm; i++)
                    {
                        var m = new MatDef { Key = r.ReadString(), Kind = r.ReadString(), Tex = r.ReadString() };
                        r.ReadString();
                        m.Col = new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        matDefs[m.Key] = m;
                    }
                    int np = r.ReadInt32();
                    for (int i = 0; i < np; i++)
                    {
                        string key = r.ReadString();
                        int nv = r.ReadInt32();
                        var p = new Vector3[nv]; var nn = new Vector3[nv]; var uv = new Vector2[nv];
                        for (int v = 0; v < nv; v++) p[v] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        for (int v = 0; v < nv; v++) nn[v] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        for (int v = 0; v < nv; v++) uv[v] = new Vector2(r.ReadSingle(), r.ReadSingle());
                        int ni = r.ReadInt32();
                        var idx = new int[ni];
                        for (int k = 0; k < ni; k++) idx[k] = r.ReadInt32();
                        var mesh = new Mesh { name = "vaz_" + key };
                        mesh.vertices = p; mesh.normals = nn; mesh.uv = uv; mesh.triangles = idx;
                        mesh.RecalculateBounds();
                        list.Add(new Part { Mat = key, Mesh = mesh });
                    }
                }
            meshCache[file] = list;
            return list;
        }

        static Dictionary<string, Vector3> Info()
        {
            if (info != null) return info;
            info = new Dictionary<string, Vector3>();
            string f = Path.Combine(Dir, "infos.txt");
            if (!File.Exists(f)) return info;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach (string l in File.ReadAllLines(f))
            {
                string[] p = l.Split(' ');
                float x, y, z;
                if (p.Length >= 4 && float.TryParse(p[1], System.Globalization.NumberStyles.Float, ci, out x) && float.TryParse(p[2], System.Globalization.NumberStyles.Float, ci, out y)
                    && float.TryParse(p[3], System.Globalization.NumberStyles.Float, ci, out z)) info[p[0]] = new Vector3(x, y, z);
            }
            return info;
        }

        static Texture2D Tex(string name, bool clamp = false)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Texture2D t;
            if (texCache.TryGetValue(name, out t)) return t;
            string f = Path.Combine(Dir, name + ".png");
            t = null;
            if (File.Exists(f))
            {
                t = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "vaz_" + name, hideFlags = HideFlags.HideAndDontSave };
                t.LoadImage(File.ReadAllBytes(f));
                t.wrapMode = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat; t.anisoLevel = 4;
            }
            else Log.Warn("vaz : texture " + name + ".png absente");
            texCache[name] = t;
            return t;
        }

        // ---------------------------------------------------------------- matieres
        static Shader transp, cutout, unlitTex, unlitTransp;
        static Shader FindShader(params string[] names) { foreach (string n in names) { Shader s = Shader.Find(n); if (s != null) return s; } return null; }

        static Material MaterialFor(Built b, string key, Material basePaint, Color paint, int number)
        {
            Material m;
            if (b.Mats.TryGetValue(key, out m)) return m;
            if (transp == null)
            {
                transp = FindShader("Legacy Shaders/Transparent/Diffuse", "Transparent/Diffuse", "Particles/Alpha Blended", "Unlit/Transparent");
                cutout = FindShader("Legacy Shaders/Transparent/Cutout/Diffuse", "Transparent/Cutout/Diffuse", "Unlit/Transparent Cutout");
                unlitTex = FindShader("Unlit/Texture", "Legacy Shaders/Self-Illumin/Diffuse", "Self-Illumin/Diffuse");
                unlitTransp = FindShader("Unlit/Transparent", "Particles/Additive", "Legacy Shaders/Particles/Additive");
            }
            MatDef d; matDefs.TryGetValue(key, out d);
            string kind = d != null ? d.Kind : "opaque";
            Texture2D t = key == "numero" ? Tex("numero-" + number.ToString("00"), true) : d != null ? Tex(d.Tex) : null;
            if ((kind == "glass" || kind == "fade" || kind == "lampfade") && transp != null)
            {
                m = new Material(transp) { mainTexture = kind == "glass" ? null : t };
                m.color = kind == "glass" ? d.Col : Color.white;
            }
            else if ((kind == "cutout" || kind == "lampcut") && cutout != null)
            {
                m = new Material(cutout) { mainTexture = t, color = Color.white };
                if (m.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", 0.4f);
            }
            else
            {
                m = new Material(basePaint);
                // (la peinture de la SORBET porte des cartes de salete et de detail : retirees)
                foreach (string tp in new[] { "_DetailAlbedoMap", "_DetailNormalMap", "_DetailMask", "_OcclusionMap", "_BumpMap", "_SpecGlossMap", "_ParallaxMap", "_EmissionMap" })
                    if (m.HasProperty(tp)) m.SetTexture(tp, null);
                bool shiny = kind == "paint";
                if (m.HasProperty("_SpecColor")) m.SetColor("_SpecColor", shiny ? new Color(0.24f, 0.24f, 0.24f) : new Color(0.08f, 0.08f, 0.08f));
                if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", shiny ? 0.78f : 0.35f);
                m.mainTexture = t;
                m.color = kind == "paint" ? paint : d != null ? new Color(d.Col.r, d.Col.g, d.Col.b, 1f) : Color.white;
            }
            m.name = "vaz_" + key;
            b.Mats[key] = m;
            // feux : variante allumee (sans eclairage de la scene), et pour l'arriere une variante faible (veilleuses)
            if (kind.StartsWith("lamp") && unlitTex != null)
            {
                Shader ls = kind == "lamp" ? unlitTex : unlitTransp ?? unlitTex;
                b.Lit[key] = new Material(ls) { mainTexture = t, color = Color.white, name = "vaz_" + key + "_allume" };
                if (key.EndsWith("_ar") && t != null)
                {
                    var dimTex = new Texture2D(t.width, t.height, TextureFormat.RGBA32, true) { wrapMode = t.wrapMode, name = t.name + "_faible", hideFlags = HideFlags.HideAndDontSave };
                    Color32[] px = t.GetPixels32();
                    for (int i = 0; i < px.Length; i++) { px[i].r = (byte)(px[i].r * 0.45f); px[i].g = (byte)(px[i].g * 0.45f); px[i].b = (byte)(px[i].b * 0.45f); }
                    dimTex.SetPixels32(px); dimTex.Apply();
                    b.Dim[key] = new Material(ls) { mainTexture = dimTex, color = Color.white, name = "vaz_" + key + "_faible" };
                }
            }
            return m;
        }

        static void AddParts(Built b, Transform parent, string file, Material basePaint, Color paint, int number)
        {
            foreach (Part p in Load(file))
            {
                var go = new GameObject("vaz_" + p.Mat);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = p.Mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = MaterialFor(b, p.Mat, basePaint, paint, number);
                MatDef d;
                if (matDefs.TryGetValue(p.Mat, out d) && d.Kind.StartsWith("lamp")) b.Lamps.Add(new KeyValuePair<MeshRenderer, string>(mr, p.Mat));
            }
        }

        static Material FindMat(GameObject root, string part)
        {
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                foreach (Material m in r.sharedMaterials)
                    if (m != null && m.name.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0) return m;
            return null;
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { Transform r = FindDeep(t.GetChild(i), name); if (r != null) return r; }
            return null;
        }

        static GameObject FindSorbet()
        {
            GameObject g = Game.FindAny("SORBET(190-200psi)");
            if (g != null && g.GetComponent("CarDynamics") != null) return g;
            foreach (Rigidbody rb in Object.FindObjectsOfType<Rigidbody>())
                if (rb.transform.parent == null && rb.name.StartsWith("SORBET") && rb.GetComponent("CarDynamics") != null) return rb.gameObject;
            return null;
        }

        // Voiture du rallye du jeu (RACES/.../RALLYCAR3 ; chez l'invite, Races la sort de son parent) : recherche parmi tous les
        // objets de la scene, eteints compris.
        public static GameObject RallyCar()
        {
            foreach (Transform t in Resources.FindObjectsOfTypeAll(typeof(Transform)))
                if (t != null && t.name == "RALLYCAR3" && t.gameObject.hideFlags == HideFlags.None && t.GetComponent("CarDynamics") != null) return t.gameObject;
            return null;
        }

        // ---------------------------------------------------------------- construction
        // name : nom de l'objet (le meme chez tous) ; number : 1-16 (numero et couleur). Rend null si la SORBET ou les fichiers manquent.
        public static Built Build(string name, int number, Color paint, Vector3 pos, Quaternion rot)
        {
            GameObject sorbet = FindSorbet();
            if (sorbet == null) { Log.Warn("vaz : SORBET introuvable"); return null; }
            if (!HasFiles) { Log.Warn("vaz : fichiers du modele absents (" + Dir + ")"); return null; }
            Dictionary<string, Vector3> inf = Info();
            var b = new Built();
            GameObject car = Object.Instantiate(sorbet);
            car.SetActive(false);   // (reglee eteinte : ses automates demarrent sur la version modifiee)
            car.name = name;
            b.Car = car;
            Transform ct = car.transform;
            ct.position = sorbet.transform.position; ct.rotation = sorbet.transform.rotation;

            // 1. Automates : etats Save/Load vides, SaveOptions coupe, identifiants prefixes (jamais ceux de la SORBET).
            int emptied = 0, ids = 0;
            string pre = "V" + number + "-";
            Transform saveOpt = ct.Find("SaveOptions");
            foreach (PlayMakerFSM f in car.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                if (saveOpt != null && f.transform.IsChildOf(saveOpt)) { f.enabled = false; emptied++; continue; }
                try
                {
                    f.Fsm.InitData();
                    foreach (FsmState s in f.FsmStates)
                        if (s.Name.IndexOf("Save", System.StringComparison.OrdinalIgnoreCase) >= 0 || s.Name.IndexOf("Load", System.StringComparison.OrdinalIgnoreCase) >= 0) { s.Actions = new FsmStateAction[0]; emptied++; }
                    FsmString[] strs = f.FsmVariables.StringVariables;
                    if (strs != null)
                        foreach (FsmString sv in strs)
                            if (sv != null && (sv.Name == "ID" || sv.Name.StartsWith("UniqueTag")) && !string.IsNullOrEmpty(sv.Value) && !sv.Value.StartsWith(pre)) { sv.Value = pre + sv.Value; ids++; }
                }
                catch (System.Exception e) { Log.Warn("vaz : automate " + f.FsmName + " : " + e.Message); }
            }

            // 2. Visuel de la SORBET cache (colliders et fumee d'echappement gardes), LODGroup retire, portieres arriere et hayon
            //    eteints (trois portes), degats coupes.
            foreach (Renderer rd in car.GetComponentsInChildren<Renderer>(true))
            {
                if (rd is ParticleRenderer || rd is ParticleSystemRenderer) continue;
                rd.enabled = false; b.Hidden.Add(rd);
            }
            foreach (LODGroup lg in car.GetComponentsInChildren<LODGroup>(true)) Object.Destroy(lg);
            foreach (string dp in new[] { "Doors/DoorRear(leftx)", "Doors/DoorRear(right)", "Hatch/Hatch" })
            {
                Transform d = ct.Find(dp);
                if (d != null && d.gameObject.activeSelf) d.gameObject.SetActive(false);
            }
            foreach (Component c in car.GetComponentsInChildren<Component>(true))
                if (c != null && c.GetType().Name == "Deformable" && c is Behaviour) ((Behaviour)c).enabled = false;

            // 3. Alignement : milieu des roues de la VAZ (infos.txt) sur celui de la SORBET ; essieux a l'empattement de la VAZ.
            Wheel[] wheels = car.GetComponentsInChildren<Wheel>(true);
            Vector3 sMid = Vector3.zero, vMid = Vector3.zero; int nv = 0;
            foreach (Wheel w in wheels) sMid += ct.InverseTransformPoint((w.model != null ? w.model.transform : w.transform).position);
            if (wheels.Length > 0) sMid /= wheels.Length;
            foreach (string wn in new[] { "wheel_lf", "wheel_rf", "wheel_lr", "wheel_rr" }) { Vector3 p; if (inf.TryGetValue(wn, out p)) { vMid += p; nv++; } }
            if (nv > 0) vMid /= nv;
            Vector3 offset = sMid - vMid;
            foreach (string[] ax in new[] { new[] { "FRONT", "wheel_lf", "wheel_rf" }, new[] { "REAR", "wheel_lr", "wheel_rr" } })
            {
                Transform at = ct.Find(ax[0]); Vector3 a1, a2;
                if (at == null || !inf.TryGetValue(ax[1], out a1) || !inf.TryGetValue(ax[2], out a2)) continue;
                at.localPosition = new Vector3(at.localPosition.x, at.localPosition.y, offset.z + (a1.z + a2.z) * 0.5f);
            }

            // 4. Carrosserie et capot (soude : pas de moteur dessous), portieres, volant.
            Material basePaint = FindMat(sorbet, "paint") ?? FindMat(sorbet, "");
            var body = new GameObject("VAZ");
            body.transform.SetParent(ct, false);
            body.transform.localPosition = offset; body.transform.localRotation = Quaternion.identity;
            b.Body = body.transform;
            AddParts(b, body.transform, "carrosserie.p205", basePaint, paint, number);
            AddParts(b, body.transform, "capot.p205", basePaint, paint, number);
            int doors = 0;
            foreach (string[] dd in new[] { new[] { "portiere_g.p205", "DoorFront(leftx)", "door_dside_f" }, new[] { "portiere_d.p205", "DoorFront(right)", "door_pside_f" } })
            {
                Transform sd = FindDeep(ct, dd[1]);
                if (sd == null) { Log.Warn("vaz : " + dd[1] + " de la SORBET introuvable"); continue; }
                Vector3 hl;
                if (inf.TryGetValue(dd[2], out hl))
                {
                    // charniere de la SORBET sur celle de la VAZ (x et z ; axe vertical garde), attache de l'articulation recalee
                    Vector3 was = ct.InverseTransformPoint(sd.position), lw = ct.InverseTransformPoint(body.transform.TransformPoint(hl));
                    sd.position = ct.TransformPoint(new Vector3(lw.x, was.y, lw.z));
                    var hj = sd.GetComponent<HingeJoint>();
                    if (hj != null)
                    {
                        Vector3 aw = sd.TransformPoint(hj.anchor);
                        hj.autoConfigureConnectedAnchor = false;
                        hj.connectedAnchor = hj.connectedBody != null ? hj.connectedBody.transform.InverseTransformPoint(aw) : aw;
                    }
                }
                var door = new GameObject("VAZ_" + dd[0].Replace(".p205", ""));
                door.transform.SetParent(body.transform, false);
                AddParts(b, door.transform, dd[0], basePaint, paint, number);
                door.transform.SetParent(sd, true);
                doors++;
            }
            Vector3 sc;
            if (inf.TryGetValue("volant_centre", out sc))
            {
                b.SteerPivot = new GameObject("VAZ_volant").transform;
                b.SteerPivot.SetParent(body.transform, false);
                b.SteerPivot.localPosition = sc;
                var inner = new GameObject("pieces").transform;
                inner.SetParent(b.SteerPivot, false);
                inner.localPosition = -sc;
                AddParts(b, inner, "volant.p205", basePaint, paint, number);
                Vector3 axv;
                if (inf.TryGetValue("volant_axe", out axv) && axv.sqrMagnitude > 0.1f) b.SteerAxis = axv.normalized;
                b.SorbetSteer = FindDeep(ct, "SteeringPivotSorbett");
                if (b.SorbetSteer != null) b.SorbetSteerRest = b.SorbetSteer.localRotation;
                b.Axis = car.GetComponent("AxisCarController");
            }

            // 5. Roues visibles : jantes et pneus de RALLYCAR3 sur les modeles de roue de la SORBET, au rayon de la SORBET, a la
            //    voie de la VAZ (roue visible decalee le long de l'essieu ; les roues physiques ne bougent pas).
            GameObject rc = RallyCar();
            int rw = 0;
            Wheel[] rcw = rc != null ? rc.GetComponentsInChildren<Wheel>(true) : new Wheel[0];
            foreach (Wheel w in wheels)
            {
                Wheel mate = null;
                foreach (Wheel x in rcw) if (x.wheelPos == w.wheelPos) { mate = x; break; }
                GameObject rm = mate != null ? mate.model : null;
                if (rm == null) continue;
                Transform model = w.model != null ? w.model.transform : w.transform;
                GameObject cp = CopyVisual(rm, model, "VAZ_roue");
                float k = mate.radius > 0.05f ? w.radius / mate.radius : 1f;
                Vector3 ls = rm.transform.lossyScale * k, ms = model.lossyScale;
                cp.transform.localPosition = Vector3.zero; cp.transform.localRotation = Quaternion.identity;
                cp.transform.localScale = new Vector3(ls.x / ms.x, ls.y / ms.y, ls.z / ms.z);
                Vector3 lp = ct.InverseTransformPoint(model.position), target;
                string wn = "wheel_" + (lp.x < 0 ? "l" : "r") + (lp.z > 0 ? "f" : "r");
                if (inf.TryGetValue(wn, out target)) cp.transform.position += ct.right * ((offset.x + target.x) - lp.x);
                rw++;
            }

            // 6. Poste de conduite : zone d'entree, pivot de tete, SpawnPlayer, MassDriver et colonne de direction de la SORBET
            //    glissent du meme vecteur (tete de la SORBET -> tete du pilote de la VAZ).
            Transform hp = FindDeep(ct, "DriverHeadPivot");
            Vector3 delta = Vector3.zero;
            if (hp != null)
            {
                delta = (offset + HeadModel) - ct.InverseTransformPoint(hp.position);
                foreach (string mp in new[] { "Functions/PlayerTrigger/DriveTriggerX", "Functions/PlayerTrigger/DriverHeadPivot", "Functions/PlayerTrigger/SpawnPlayer", "Functions/MassDriver", "Functions/SteeringColumnPivot" })
                {
                    Transform m = ct.Find(mp);
                    if (m != null) m.position = ct.TransformPoint(ct.InverseTransformPoint(m.position) + delta);
                }
                foreach (Joint j in hp.GetComponents<Joint>())
                {
                    Vector3 aw = hp.TransformPoint(j.anchor);
                    j.autoConfigureConnectedAnchor = false;
                    j.connectedAnchor = j.connectedBody != null ? j.connectedBody.transform.InverseTransformPoint(aw) : aw;
                }
            }

            // 6b. Commandes du tableau de bord et frein a main sur ceux du modele : chaque objet du jeu deplace pour que le centre de
            //     ses colliders tombe sur la piece de la VAZ.
            int ctl = 0;
            foreach (KeyValuePair<string, Vector3> kv in Controls)
            {
                Transform t = ct.Find(kv.Key);
                if (t == null) { Log.Warn("vaz : commande " + kv.Key + " introuvable"); continue; }
                if (MoveOnto(t, body.transform.TransformPoint(kv.Value))) ctl++;
            }
            Transform lever = ct.Find("Functions/HandBrake/LeverPivot");
            if (lever != null)
            {
                MoveOnto(lever, body.transform.TransformPoint(BrakeGripModel));
                b.SorbetBrake = lever; b.SorbetBrakeRest = lever.localRotation;
                b.BrakePivot = new GameObject("VAZ_frein").transform;
                b.BrakePivot.SetParent(body.transform, false);
                b.BrakePivot.localPosition = BrakePivotModel;
                var inner = new GameObject("pieces").transform;
                inner.SetParent(b.BrakePivot, false);
                inner.localPosition = -BrakePivotModel;
                AddParts(b, inner, "frein.p205", basePaint, paint, number);
            }
            else Log.Warn("vaz : frein a main du jeu (Functions/HandBrake/LeverPivot) introuvable");

            // 7. Lumieres de la SORBET sur les feux de la VAZ ; listes pour allumer les feux du modele.
            int lights = 0;
            foreach (Light l in car.GetComponentsInChildren<Light>(true))
            {
                Vector3 lp = ct.InverseTransformPoint(l.transform.position);
                if (l.name.Contains("Brake")) b.Brake.Add(l);
                else if (l.name.Contains("Reverse")) b.Reverse.Add(l);
                else if (l.type == LightType.Spot && lp.z > 0.5f) b.Front.Add(l);
                else if (lp.z < -0.5f && l.color.r > l.color.g * 1.5f) b.Rear.Add(l);
                if (Mathf.Abs(lp.z) < 1.2f) continue;   // (voyants du tableau de bord, plafonnier)
                bool orange = l.color.r > 0.8f && l.color.g > 0.35f && l.color.g < 0.8f && l.color.b < 0.3f;
                bool red = l.color.r > l.color.g * 1.8f && !orange;
                string side = lp.x < 0 ? "l" : "r", bone = null;
                if (lp.z > 0.5f) bone = l.type == LightType.Spot || (!red && !orange) ? "headlight_" + side : null;
                else if (lp.z < -0.5f && red) bone = "taillight_" + side;
                Vector3 target;
                if (bone == null || !inf.TryGetValue(bone, out target)) continue;
                l.transform.position = body.transform.TransformPoint(target) + ct.forward * (lp.z > 0 ? 0.08f : -0.06f);
                lights++;
            }

            // 8. Mecanique, place, allumee ; VehicleSync la releve tout de suite.
            b.Rb = car.GetComponent<Rigidbody>();
            b.Dt = car.GetComponent<Drivetrain>();
            VazHandling.Apply(b, true);
            var eng = new GameObject("VAZ_moteur_son").transform;
            eng.SetParent(body.transform, false); eng.localPosition = EngineModel;
            b.Sound = VazSound.Create(car, eng);
            Transform tank = ct.Find("Simulation/FuelTankSorbett");
            PlayMakerFSM tf = tank != null ? Game.FsmOn(tank.gameObject, "Data") : null;
            FsmFloat fuel = tf != null ? tf.FsmVariables.FindFsmFloat("FuelLevel") : null;
            if (fuel != null) fuel.Value = 40f;
            ct.position = pos; ct.rotation = rot;
            if (b.Rb != null) { b.Rb.velocity = Vector3.zero; b.Rb.angularVelocity = Vector3.zero; }
            car.SetActive(true);
            VehicleSync.RescanSoon();
            b.NextMask = 0f;
            Log.Info("vaz : " + name + " construite en " + pos.ToString("F1") + " : " + emptied + " etats de sauvegarde vides, " + ids + " identifiants prefixes, " + b.Hidden.Count + " rendus SORBET caches, "
                     + doors + " portieres, " + ctl + " commandes posees, " + rw + " roues du rallye" + (rc == null ? " (RALLYCAR3 introuvable)" : "") + ", " + lights + " lumieres posees, " + b.Lamps.Count + " pieces de feux, decalage " + offset.ToString("F2") + ", tete " + delta.ToString("F2"));
            return b;
        }

        // Copie d'un sous-arbre reduite a Transform + MeshFilter + MeshRenderer, tout allume.
        static GameObject CopyVisual(GameObject src, Transform parent, string name)
        {
            GameObject cp = Object.Instantiate(src);
            cp.name = name;
            for (int pass = 0; pass < 3; pass++)
                foreach (Component c in cp.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || c is Transform || c is MeshFilter || c is MeshRenderer) continue;
                    try { Object.DestroyImmediate(c); } catch { }
                }
            foreach (Transform t in cp.GetComponentsInChildren<Transform>(true)) t.gameObject.SetActive(true);
            cp.transform.SetParent(parent, false);
            return cp;
        }

        // ---------------------------------------------------------------- vie de la voiture (chaque image)
        public static void Tick(Built b, float now)
        {
            if (b.Car == null) return;
            if (now >= b.NextHide)
            {
                b.NextHide = now + 0.5f;
                foreach (Renderer rd in b.Hidden) if (rd != null && rd.enabled) rd.enabled = false;   // (des automates rallument feux, vitres givrees...)
            }
            Steering(b);
            Brake(b);
            if (b.Sound != null) b.Sound.Update();
            Lamps(b, now);
            VazHandling.Tick(b, now);
            if (now >= b.NextMask) { b.NextMask = now + 1f; WheelMask(b); }
        }

        // Objet du jeu deplace (sans le tourner) pour que le centre de ses colliders soit en 'world'.
        static bool MoveOnto(Transform t, Vector3 world)
        {
            // (voiture encore eteinte : Collider.bounds est vide -> centre local de chaque collider, moyenne)
            Vector3 sum = Vector3.zero; int n = 0;
            foreach (Collider k in t.GetComponentsInChildren<Collider>(true))
            {
                Vector3 lc = k is BoxCollider ? ((BoxCollider)k).center : k is SphereCollider ? ((SphereCollider)k).center : k is CapsuleCollider ? ((CapsuleCollider)k).center
                           : k is MeshCollider && ((MeshCollider)k).sharedMesh != null ? ((MeshCollider)k).sharedMesh.bounds.center : Vector3.zero;
                sum += k.transform.TransformPoint(lc); n++;
            }
            Vector3 c = n > 0 ? sum / n : t.position;
            t.position += world - c;
            return n > 0;
        }

        // Levier du modele : angle du levier du jeu (repos -> tire), poignee qui monte.
        static void Brake(Built b)
        {
            if (b.BrakePivot == null || b.SorbetBrake == null) return;
            Quaternion d = Quaternion.Inverse(b.SorbetBrakeRest) * b.SorbetBrake.localRotation;
            float a; Vector3 ax;
            d.ToAngleAxis(out a, out ax);
            if (a > 180f) a = 360f - a;
            b.BrakePivot.localRotation = Quaternion.AngleAxis(-a * Config.GetFloat("Test", "VazFreinSens", 1f), Vector3.right);
        }

        static System.Reflection.FieldInfo steerField;
        // Volant : angle de celui de la SORBET (cache), autour de l'axe de la colonne de la VAZ.
        static void Steering(Built b)
        {
            if (b.SteerPivot == null || b.SorbetSteer == null) return;
            // amplitude : celle du volant de la SORBET ; sens : celui de la direction du controleur (comme les voitures du pack
            // prive de JD : l'axe du quaternion change de signe d'un cote a l'autre)
            Quaternion d = Quaternion.Inverse(b.SorbetSteerRest) * b.SorbetSteer.localRotation;
            float a; Vector3 ax;
            d.ToAngleAxis(out a, out ax);
            if (a > 180f) a = 360f - a;
            float input = 0f;
            if (b.Axis != null)
            {
                if (steerField == null) steerField = b.Axis.GetType().GetField("steering");
                object v = steerField != null ? steerField.GetValue(b.Axis) : null;
                if (v is float) input = (float)v;
            }
            b.SteerPivot.localRotation = Quaternion.AngleAxis(-a * Mathf.Sign(input), b.SteerAxis);
        }

        static bool AnyOn(List<Light> ls)
        {
            foreach (Light l in ls) if (l != null && l.enabled && l.gameObject.activeInHierarchy && l.intensity > 0.01f) return true;
            return false;
        }

        // Feux du modele : allumes (sans eclairage) quand les lumieres de la copie le sont ; arriere faible avec les feux, fort au
        // freinage.
        static void Lamps(Built b, float now)
        {
            if (now < b.NextLamp) return;
            b.NextLamp = now + 0.1f;
            bool f = AnyOn(b.Front), r = AnyOn(b.Rear) || f, br = AnyOn(b.Brake), rv = AnyOn(b.Reverse);
            if (b.LampsInit && f == b.FrontOn && r == b.RearOn && br == b.BrakeOn && rv == b.ReverseOn) return;
            b.LampsInit = true; b.FrontOn = f; b.RearOn = r; b.BrakeOn = br; b.ReverseOn = rv;
            foreach (KeyValuePair<MeshRenderer, string> kv in b.Lamps)
            {
                if (kv.Key == null) continue;
                string k = kv.Value;
                Material m = null;
                if (k.EndsWith("_ar")) { if (br) b.Lit.TryGetValue(k, out m); else if (r && !b.Dim.TryGetValue(k, out m)) b.Lit.TryGetValue(k, out m); }
                else if ((k.EndsWith("_av") && f) || (k.EndsWith("_re") && rv)) b.Lit.TryGetValue(k, out m);
                kv.Key.sharedMaterial = m ?? b.Mats[k];
            }
        }

        // Rayons des roues : couches des colliders de la voiture croises par la course d'une roue retirees de leur masque (voir
        // Jokkis.WheelMask : essieux deplaces, un collider de caisse dans la course d'une roue fait tourner la voiture sur elle-meme).
        static void WheelMask(Built b)
        {
            Transform ct = b.Car.transform;
            Wheel[] wheels = b.Car.GetComponentsInChildren<Wheel>(true);
            int own = 0;
            foreach (Wheel w in wheels)
            {
                float travel = w.suspensionTravel > 0f ? w.suspensionTravel : 0.2f, rad = w.radius > 0f ? w.radius : 0.3f;
                for (float d = 0f; d <= travel + rad + 0.001f; d += 0.05f)
                    foreach (Collider c in Physics.OverlapSphere(w.transform.position - ct.up * d, 0.06f))
                    {
                        if (c == null || !c.transform.IsChildOf(ct)) continue;
                        int l = c.gameObject.layer;
                        string ln = LayerMask.LayerToName(l);
                        if (l == b.Car.layer || l == 0 || ln == "Terrain" || ln == "Water") continue;
                        own |= 1 << l;
                    }
            }
            if (own == 0) return;
            System.Reflection.FieldInfo lm = typeof(Wheel).GetField("layerMask", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (lm == null) return;
            foreach (Wheel w in wheels)
            {
                object v = lm.GetValue(w);
                int m = v is LayerMask ? ((LayerMask)v).value : v is int ? (int)v : 0;
                if ((m & own) == 0) continue;
                int nm = m & ~own;
                if (v is LayerMask) { LayerMask lk = (LayerMask)v; lk.value = nm; lm.SetValue(w, lk); } else lm.SetValue(w, nm);
                if (b.MaskLogs++ < 4) Log.Info("vaz : roue " + w.wheelPos + " : masque " + m + " -> " + nm);
            }
        }

        // Voiture detruite : ce qui n'est pas a elle en sort d'abord (joueur, cameras des retroviseurs, objets MWCoop).
        public static void Destroy(Built b)
        {
            if (b == null || b.Car == null) return;
            Transform ct = b.Car.transform;
            if (VehicleSync.LocalDrivingRoot == ct) VehicleSync.ExitLocal();
            GameObject mirrors = Game.FindAny("Systems/Mirrors/Disable");
            foreach (Transform t in b.Car.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t == ct) continue;
                if (t.name == "PLAYER") t.SetParent(null, true);
                else if (t.name.EndsWith("MirrorCam")) t.SetParent(mirrors != null ? mirrors.transform : null, true);
                else if (t.name.StartsWith("MWCoop-")) t.SetParent(null, true);
            }
            foreach (Material m in b.Mats.Values) if (m != null) Object.Destroy(m);
            foreach (Material m in b.Lit.Values) if (m != null) Object.Destroy(m);
            foreach (Material m in b.Dim.Values) if (m != null) { Object.Destroy(m.mainTexture); Object.Destroy(m); }
            Object.Destroy(b.Car);
            b.Car = null;
        }

        // ---------------------------------------------------------------- apercu (F10)
        // Copie visuelle seule (carrosserie, capot, portieres, volant, roues du rallye aux centres des roues), sur cette couche,
        // a l'origine ; matieres a liberer avec DestroyPreview.
        public static GameObject BuildPreview(int number, Color paint, int layer, out Built holder)
        {
            holder = null;
            GameObject sorbet = FindSorbet();
            if (sorbet == null || !HasFiles) return null;
            Material basePaint = FindMat(sorbet, "paint") ?? FindMat(sorbet, "");
            var b = new Built();
            var root = new GameObject("MWCoop-VazApercu-" + number);
            b.Car = root;
            foreach (string f in new[] { "carrosserie.p205", "capot.p205", "portiere_g.p205", "portiere_d.p205", "volant.p205", "frein.p205" }) AddParts(b, root.transform, f, basePaint, paint, number);
            GameObject rc = RallyCar();
            Dictionary<string, Vector3> inf = Info();
            if (rc != null)
                foreach (Wheel w in rc.GetComponentsInChildren<Wheel>(true))
                {
                    if (w.model == null) continue;
                    string wp = w.wheelPos.ToString();
                    string wn = "wheel_" + (wp.Contains("LEFT") ? "l" : "r") + (wp.StartsWith("FRONT") ? "f" : "r");
                    Vector3 c;
                    if (!inf.TryGetValue(wn, out c)) continue;
                    GameObject cp = CopyVisual(w.model, root.transform, "roue" + wp);
                    cp.transform.localPosition = c;
                    cp.transform.localRotation = Quaternion.Inverse(rc.transform.rotation) * w.model.transform.rotation;
                    float k = w.radius > 0.05f ? 0.29f / w.radius : 1f;   // (rayon des roues de la VAZ)
                    Vector3 ls = w.model.transform.lossyScale * k;
                    cp.transform.localScale = ls;
                }
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            holder = b;
            return root;
        }

        public static void DestroyPreview(Built b)
        {
            if (b == null) return;
            foreach (Material m in b.Mats.Values) if (m != null) Object.Destroy(m);
            foreach (Material m in b.Lit.Values) if (m != null) Object.Destroy(m);
            foreach (Material m in b.Dim.Values) if (m != null) { Object.Destroy(m.mainTexture); Object.Destroy(m); }
            if (b.Car != null) Object.Destroy(b.Car);
            b.Car = null;
        }
    }
}
