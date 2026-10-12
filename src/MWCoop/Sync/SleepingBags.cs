using System;
using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Sacs de couchage transportables (demande de JD, 10/10 ; modeles plie / deplie fournis, assets/sac, convertis de FBX
    // par run/fbx2mesh.py). Un sac par joueur au moins, poses a cote de l'hote quand il en manque.
    //  - plie : objet du jeu (couche 19, etiquette PART) -- pris en main, lance, pose dans les voitures ; sa pose suit Props
    //    (cle « sac:<id> ») ;
    //  - pose au sol, F (touche Utiliser) : il se deplie dans l'axe du regard, immobile ; on y dort en tenant F comme dans un
    //    lit -- c'est le declencheur de sommeil du canape du jeu (EQUIPMENTS/sofa(itemx)/Sleep, point de sommeil mobile),
    //    copie sur le sac : fatigue, temps qui passe, reveil, tout est celui du jeu ;
    //  - deplie, F appuye brievement : il se replie (pas pendant qu'on dort).
    // L'hote garde la liste (MWCoop-sacs.txt a cote de la sauvegarde, ecrit a chaque sauvegarde du jeu) et l'envoie aux
    // invites (@sacs) ; plier / deplier part a tous (@sac).
    public static class SleepingBags
    {
        class Bag
        {
            public string Id; public GameObject Go; public Rigidbody Rb; public MeshFilter Mf; public BoxCollider Col;
            public Transform Sleep; public PlayMakerFSM Fsm; public bool Folded = true;
            public bool Parked;   // plie, loin de tous : tenu immobile (le sol d'un batiment eteint au loin ne le porte pas)
        }
        static readonly List<Bag> bags = new List<Bag>();
        static Mesh mFold, mUnfold;
        static Material mat;
        static GameObject sleepTpl;
        static bool prepared, listed;
        static float startAt = -1, nextHost, nextSave, fDownAt = -1;
        static Bag looked, fBag;
        static FsmBool guiUse; static FsmString guiText;
        const float FoldScale = 0.32f;    // (modele plie fourni a 1 m de diametre : ramene a 32 cm x 49 cm)

        public static void OnLevelLoaded()
        {
            bags.Clear(); sleepTpl = null; prepared = false; shot = 0; testStep = 0; pickStep = 0; hand = null; listed = false; looked = null; fBag = null; fDownAt = -1;
            startAt = PlayerSync.InGame ? Time.realtimeSinceStartup + 15f : -1;
        }

        // Cle Props d'un sac (Props.Scan) ; null sinon.
        public static string KeyOf(Rigidbody rb)
        {
            foreach (Bag b in bags) if (b.Rb == rb) return "sac:" + b.Id;
            return null;
        }

        // ---------------------------------------------------------------- modeles
        public static Mesh LoadMesh(string res, float scale, bool flip = false)
        {
            using (Stream s = typeof(SleepingBags).Assembly.GetManifestResourceStream(res))
            using (var r = new BinaryReader(s))
            {
                if (new string(r.ReadChars(4)) != "MWM1") throw new Exception("maillage " + res + " : format inconnu");
                int nSub = r.ReadInt32(), n = r.ReadInt32();
                var pos = new Vector3[n]; var nrm = new Vector3[n]; var uv = new Vector2[n];
                for (int i = 0; i < n; i++) pos[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()) * scale;
                for (int i = 0; i < n; i++) nrm[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                for (int i = 0; i < n; i++) uv[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
                if (flip)
                {
                    // (retourne : demi-tour autour de l'axe de la longueur, puis pose au sol)
                    float top = float.MinValue;
                    foreach (Vector3 p in pos) top = Mathf.Max(top, p.y);
                    for (int i = 0; i < n; i++) { pos[i] = new Vector3(-pos[i].x, top - pos[i].y, pos[i].z); nrm[i] = new Vector3(-nrm[i].x, -nrm[i].y, nrm[i].z); }
                }
                var m = new Mesh { name = res };
                m.vertices = pos; m.normals = nrm; m.uv = uv;
                m.subMeshCount = nSub;
                for (int k = 0; k < nSub; k++)
                {
                    int c = r.ReadInt32(); var idx = new int[c];
                    for (int i = 0; i < c; i++) idx[i] = r.ReadInt32();
                    m.SetTriangles(idx, k);
                    r.ReadSingle(); r.ReadSingle(); r.ReadSingle();   // (couleur : celle du materiau, plus bas)
                }
                m.RecalculateBounds();
                return m;
            }
        }

        static bool Prepare()
        {
            if (prepared) return sleepTpl != null;
            prepared = true;
            try
            {
                mFold = LoadMesh("MWCoop.sac-plie.mwm", FoldScale);
                mUnfold = LoadMesh("MWCoop.sac-deplie.mwm", 1f, Config.GetInt("Test", "SacRetourne", 1) != 0);   // (modele fourni a l'envers : oreiller dessous)
                GameObject sofa = Game.FindAny("EQUIPMENTS/sofa(itemx)");
                Transform sl = sofa != null ? sofa.transform.Find("Sleep") : null;
                if (sl == null) { GameObject bed = Game.FindAny("YARD/Building/BEDROOM1/LOD_bedroom1/Sleep 1"); sl = bed != null ? bed.transform : null; }
                if (sl == null) { Log.Warn("sacs de couchage : declencheur de sommeil du jeu introuvable"); return false; }
                sleepTpl = sl.gameObject;
                Renderer src = sofa != null ? sofa.GetComponent<Renderer>() : null;
                mat = src != null ? new Material(src.sharedMaterial) : new Material(Shader.Find("Diffuse"));
                var tex = new Texture2D(2, 2);
                using (Stream s = typeof(SleepingBags).Assembly.GetManifestResourceStream("MWCoop.sac.jpg"))
                {
                    var buf = new byte[s.Length]; s.Read(buf, 0, buf.Length);
                    tex.LoadImage(buf);
                }
                mat.mainTexture = tex;
                mat.color = ParseColor(Config.Get("Test", "SacCouleur", "0.22,0.42,0.26"));
                Log.Info("sacs de couchage : modeles charges (" + mFold.vertexCount + " / " + mUnfold.vertexCount + " sommets), sommeil d'apres " + Recon.Path(sl));
                return true;
            }
            catch (Exception e) { Log.Error("sacs de couchage : " + e); sleepTpl = null; return false; }
        }

        static Vector3 ParseV(string s)
        {
            string[] c = s.Split(',');
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float x = 0, y = 0, z = 0;
            if (c.Length == 3) { float.TryParse(c[0], System.Globalization.NumberStyles.Float, ci, out x); float.TryParse(c[1], System.Globalization.NumberStyles.Float, ci, out y); float.TryParse(c[2], System.Globalization.NumberStyles.Float, ci, out z); }
            return new Vector3(x, y, z);
        }

        static Color ParseColor(string s)
        {
            string[] c = s.Split(',');
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float r = 0.22f, g = 0.42f, b = 0.26f;
            if (c.Length == 3) { float.TryParse(c[0], System.Globalization.NumberStyles.Float, ci, out r); float.TryParse(c[1], System.Globalization.NumberStyles.Float, ci, out g); float.TryParse(c[2], System.Globalization.NumberStyles.Float, ci, out b); }
            return new Color(r, g, b, 1f);
        }

        static Bag Create(string id, Vector3 pos, Quaternion rot)
        {
            var go = new GameObject("sleeping bag(itemx)");
            go.transform.position = pos; go.transform.rotation = rot;
            var b = new Bag { Id = id, Go = go };
            // Modele sur un enfant toujours dessine (couche 0) : deplie, la racine passe sur « Ignore Raycast » (2), que la camera
            // du joueur (FPSCamera) ne dessine pas -- le sac deplie etait invisible en jeu (retour de JD, 10/10 ; les captures
            // d'essai passaient par une camera qui voit tout).
            var model = new GameObject("Model");
            model.layer = 0;
            model.transform.SetParent(go.transform, false);
            b.Mf = model.AddComponent<MeshFilter>();
            model.AddComponent<MeshRenderer>().sharedMaterial = mat;
            b.Col = go.AddComponent<BoxCollider>();
            b.Rb = go.AddComponent<Rigidbody>();
            b.Rb.mass = 2.5f;
            // Sommeil : declencheur du canape copie (automate, pivot anime) ; le joueur s'allonge dans la longueur du sac.
            var sl = (GameObject)UnityEngine.Object.Instantiate(sleepTpl);
            sl.name = "Sleep";
            b.Sleep = sl.transform;
            b.Sleep.SetParent(go.transform, false);
            b.Fsm = null;
            foreach (PlayMakerFSM f in sl.GetComponentsInChildren<PlayMakerFSM>(true)) if (f.FsmName == "Activate") b.Fsm = f;
            Transform pivot = b.Sleep.Find("AnimPivotSleep"), trig = b.Fsm != null ? b.Fsm.transform : null;
            foreach (Renderer r in sl.GetComponentsInChildren<Renderer>(true)) r.enabled = false;   // (reperes du corps : invisibles)
            if (pivot != null)
            {
                // Sens du corps : du pivot vers la tete (sphere), ramene dans l'axe du sac (+z).
                Transform head = pivot.Find("Sphere");
                Vector3 h = head != null ? sl.transform.InverseTransformPoint(head.position) - sl.transform.InverseTransformPoint(pivot.position) : Vector3.forward;
                h.y = 0f;
                Quaternion turn = h.sqrMagnitude > 1e-4f ? Quaternion.FromToRotation(h.normalized, Vector3.forward) : Quaternion.identity;
                b.Sleep.localRotation = turn;
                // Pivot pose a l'arriere du sac (pieds), a hauteur d'allonge : le corps (pivot -> tete, ~1,4 m) au milieu.
                Vector3 pl = turn * sl.transform.InverseTransformPoint(pivot.position);
                float len = h.magnitude;
                b.Sleep.localPosition = new Vector3(0f, Config.GetFloat("Test", "SacHauteur", 0.42f), -len * 0.5f) - new Vector3(pl.x, pl.y, pl.z)
                                        // (l'animation du jeu deplace le pivot : allonge, le joueur etait a 0,8 m sur le cote du sac et 30 cm
                                        // trop haut -- mesure, essai SacDormir=1 : pieds (-0,81 ; 0,55 ; -0,54), yeux (-0,76 ; 0,55 ; 0,86))
                                        + ParseV(Config.Get("Test", "SacDecalage", "0.79,-0.27,-0.16"));
            }
            if (trig != null)
            {
                trig.localPosition = b.Sleep.InverseTransformPoint(go.transform.TransformPoint(new Vector3(0f, 0.25f, 0f)));
                var sc = trig.GetComponent<SphereCollider>();
                if (sc != null) sc.radius = 0.55f / Mathf.Max(0.01f, trig.lossyScale.x);
            }
            if (b.Fsm != null)
            {
                FsmGameObject dyn = b.Fsm.FsmVariables.FindFsmGameObject("DynamicSleepPoint1");
                if (dyn != null) dyn.Value = go;
            }
            ApplyFolded(b, true, false);
            if (!NearPlayer(pos, 40f)) { b.Parked = true; b.Rb.isKinematic = true; }   // (pose loin de tous : tenu jusqu'a l'arrivee de quelqu'un)
            bags.Add(b);
            Props.SoonScan();
            return b;
        }

        // Plie : objet du jeu ; deplie : immobile, ni pris ni vise (couche « Ignore Raycast » : le declencheur de sommeil
        // dessus reste visable), avec son lit.
        static void ApplyFolded(Bag b, bool folded, bool log)
        {
            b.Folded = folded;
            b.Mf.sharedMesh = folded ? mFold : mUnfold;
            Bounds bb = b.Mf.sharedMesh.bounds;
            b.Col.center = bb.center;
            b.Col.size = folded ? bb.size : new Vector3(bb.size.x, 0.06f, bb.size.z);
            b.Go.layer = folded ? 19 : 2;
            b.Go.tag = folded ? "PART" : "Untagged";
            b.Rb.isKinematic = !folded;
            if (b.Sleep != null) b.Sleep.gameObject.SetActive(!folded);
            if (log) Log.Info("sac de couchage " + b.Id + (folded ? " plie" : " deplie") + " en " + b.Go.transform.position.ToString("F1"));
        }

        static Bag Find(string id) { foreach (Bag b in bags) if (b.Id == id) return b; return null; }

        // ---------------------------------------------------------------- boucle
        public static void Update()
        {
            if (!Session.Active || !PlayerSync.InGame || startAt < 0 || Time.realtimeSinceStartup < startAt) return;
            float now = Time.realtimeSinceStartup;
            if (Session.IsHost)
            {
                if (!listed)
                {
                    listed = true;
                    if (!Prepare()) return;
                    Load();
                }
                if (sleepTpl == null) return;
                EnsureCount();
                if (now >= nextHost) { nextHost = now + 10f; SendList(); }
                if (now >= nextSave) { nextSave = now + 60f; Save(); }
            }
            if (bags.Count == 0) return;
            bool park = now >= nextPark;
            if (park) nextPark = now + 1f;
            foreach (Bag b in bags)
            {
                if (b.Go == null) continue;
                if (park && b.Folded && b.Go.transform.parent == null)
                {
                    bool near = NearPlayer(b.Go.transform.position, 40f);
                    if (!near && !b.Parked && !b.Rb.isKinematic && b.Rb.velocity.sqrMagnitude < 0.01f) { b.Parked = true; b.Rb.isKinematic = true; }
                    else if (near && b.Parked) { b.Parked = false; b.Rb.isKinematic = false; }
                }
                // (deplie : le jeu remet l'etiquette PART au reveil, Props peut rendre la physique -- tenu immobile)
                if (!b.Folded) { if (!b.Rb.isKinematic) b.Rb.isKinematic = true; if (b.Go.tag != "Untagged") b.Go.tag = "Untagged"; if (b.Go.layer != 2) b.Go.layer = 2; }
            }
            Interact(now);
        }

        static float nextPark;
        static bool NearPlayer(Vector3 p, float r)
        {
            Camera cam = Camera.main;
            if (cam != null && (cam.transform.position - p).sqrMagnitude < r * r) return true;
            foreach (Avatar a in PlayerSync.Avatars) if (a.Root != null && (a.Root.transform.position - p).sqrMagnitude < r * r) return true;
            return false;
        }

        static bool KeyDown() { return Input.GetKeyDown(KeyCode.F); }
        static bool KeyUp() { return Input.GetKeyUp(KeyCode.F); }

        static void Interact(float now)
        {
            if (guiUse == null) { guiUse = FsmVariables.GlobalVariables.FindFsmBool("GUIuse"); guiText = FsmVariables.GlobalVariables.FindFsmString("GUIinteraction"); }
            Camera cam = Camera.main;
            Bag hit = null;
            if (cam != null && !Game.GlobalBool("PlayerSleeps") && !Game.GlobalBool("PlayerSeated"))
            {
                var ray = new Ray(cam.transform.position, cam.transform.forward);
                float best = 2f;
                foreach (Bag b in bags)
                {
                    if (b.Go == null || b.Go.transform.parent != null) continue;   // (tenu en main)
                    RaycastHit h;
                    if (b.Col.Raycast(ray, out h, best)) { best = h.distance; hit = b; }
                }
            }
            if (hit != looked)
            {
                if (looked != null && guiUse != null) { guiUse.Value = false; guiText.Value = ""; }
                looked = hit;
            }
            if (hit != null && guiUse != null)
            {
                guiUse.Value = true;
                // (plie : le clic gauche le prend comme tout objet du jeu -- la main du jeu, etiquette PART ; F le deplie)
                guiText.Value = hit.Folded ? Lang.T("SAC DE COUCHAGE - CLIC : PRENDRE - F : DEPLIER", "SLEEPING BAG - CLICK: PICK UP - F: UNROLL")
                                           : Lang.T("F : REPLIER - TENIR F : DORMIR", "F: ROLL UP - HOLD F: SLEEP");
            }
            if (KeyDown() && hit != null) { fDownAt = now; fBag = hit; }
            PickAssist(hit, now);
            if (fBag != null && fBag.Folded && fDownAt >= 0)
            {
                // Plie : deplie tout de suite (au sol, a l'arret, pas dans une voiture).
                Bag b = fBag; fBag = null; fDownAt = -1;
                if (b.Rb.velocity.sqrMagnitude > 0.25f || VehicleSync.CarUnder(b.Rb) != null) { Hint(Lang.T("Pose le sac par terre pour le deplier", "Put the bag on the ground to unfold it")); return; }
                Vector3 p = Ground(b.Go.transform.position);
                Vector3 fwd = cam.transform.forward; fwd.y = 0f;
                Quaternion q = Quaternion.LookRotation(fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward, Vector3.up);
                // Sol plat sous le sac deplie (au moins 2 coins a moins de 12 cm du centre) : sur un canape ou un lit, le modele rigide
                // s'enfoncait dans les coussins et le dossier (Vaydon, 0.79.2 : « le sac est a l'envers »).
                if (!Flat(p, q)) { Hint(Lang.T("Pose le sac sur un sol plat pour le deplier", "Put the bag on flat ground to unfold it")); return; }
                SetState(b, false, p, q, true);
                return;
            }
            if (fBag != null && !fBag.Folded && KeyUp())
            {
                // Deplie : appui bref -> replie (tenu : le lit du jeu l'endort).
                Bag b = fBag; float held = now - fDownAt; fBag = null; fDownAt = -1;
                if (held < 0.35f && !Game.GlobalBool("PlayerSleeps") && (b.Fsm == null || !Sleeping(b.Fsm)))
                    SetState(b, true, b.Go.transform.position + Vector3.up * 0.25f, b.Go.transform.rotation, true);
            }
        }

        // Clic gauche sur le sac plie : la main du jeu ne prend qu'a 1 m (MousePick) -- un sac par terre, vise debout, est
        // plus loin (retour de JD, 10/10 : « je n'arrive pas a le soulever »). Si elle ne l'a pas pris a l'image suivante, sa
        // prise est lancee sur le sac (RaycastHitObject puis « State 1 » : le meme enchainement que pour un objet du jeu).
        static PlayMakerFSM hand;
        static Bag clickBag;
        static int clickFrame = -1;
        static void PickAssist(Bag hit, float now)
        {
            if (Input.GetMouseButtonDown(0) && hit != null && hit.Folded) { clickBag = hit; clickFrame = Time.frameCount; return; }
            if (clickBag == null || Time.frameCount <= clickFrame) return;
            Bag b = clickBag; clickBag = null;
            if (Time.frameCount > clickFrame + 3 || b.Go == null || b.Go.transform.parent != null) return;   // (deja pris)
            if (hand == null) { GameObject h = Game.PlayerPart("Pivot/AnimPivot/Camera/FPSCamera/1Hand_Assemble/Hand"); if (h != null) hand = Game.FsmOn(h, "PickUp"); }
            if (hand == null || hand.ActiveStateName != "Look for object") return;
            FsmGameObject rh = hand.FsmVariables.FindFsmGameObject("RaycastHitObject");
            if (rh == null) return;
            rh.Value = b.Go;
            Game.SetState(hand, "State 1");
            Log.Info("sac de couchage " + b.Id + " pris en main (prise du jeu lancee : " + hand.ActiveStateName + ")");
        }

        static readonly HashSet<string> Awake = new HashSet<string> { "State 1", "Conditions?", "Wait click", "Confirm", "State 3" };
        static bool Sleeping(PlayMakerFSM f) { return !Awake.Contains(f.ActiveStateName); }

        static void Hint(string s) { try { Hud.Toast(s); } catch { } }

        static string flatInfo = "";
        static bool Flat(Vector3 p, Quaternion q)
        {
            int off = 0; flatInfo = "";
            for (int i = 0; i < 4; i++)
            {
                Vector3 c = p + q * new Vector3((i & 1) == 0 ? -0.35f : 0.35f, 0f, (i & 2) == 0 ? -0.9f : 0.9f);
                float d = Ground(c, true).y - p.y;
                flatInfo += " " + d.ToString("F2");
                if (Mathf.Abs(d) > 0.12f) off++;
            }
            return off < 3;
        }

        static Vector3 Ground(Vector3 at, bool ignoreLoose = false)
        {
            float best = float.MaxValue; Vector3 p = at;
            foreach (RaycastHit h in Physics.RaycastAll(at + Vector3.up * 0.6f, Vector3.down, 3f))
            {
                if (h.collider.isTrigger) continue;
                if (ignoreLoose && (h.collider.attachedRigidbody != null && !h.collider.attachedRigidbody.isKinematic || Game.RootName(h.collider.transform) == "PLAYER")) continue;   // (objets par terre, joueur)
                bool own = false;
                foreach (Bag b in bags) if (b.Col == h.collider) own = true;
                if (own) continue;
                if (h.distance < best) { best = h.distance; p = h.point; }
            }
            return p;
        }

        static void SetState(Bag b, bool folded, Vector3 pos, Quaternion rot, bool send)
        {
            b.Go.transform.position = pos; b.Go.transform.rotation = rot;
            b.Rb.velocity = Vector3.zero; b.Rb.angularVelocity = Vector3.zero;
            b.Parked = false;
            ApplyFolded(b, folded, true);
            if (Session.IsHost) Save();
            if (send) Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@sac").Str(b.Id).Bool(folded).Vec(pos).Quat(rot), true);
        }

        public static void OnState(int who, string id, bool folded, Vector3 pos, Quaternion rot)
        {
            if (!Prepare()) return;
            Bag b = Find(id) ?? Create(id, pos, rot);
            SetState(b, folded, pos, rot, false);
            Log.Info("sac de couchage " + id + (folded ? " plie" : " deplie") + " par #" + who);
        }

        // ---------------------------------------------------------------- liste (hote)
        static string FilePath { get { return Path.Combine(Application.persistentDataPath, "MWCoop-sacs.txt"); } }

        static void Load()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string l in File.ReadAllLines(FilePath))
                {
                    string[] c = l.Split(';');
                    if (c.Length < 9) continue;
                    var p = new Vector3(float.Parse(c[2], ci), float.Parse(c[3], ci), float.Parse(c[4], ci));
                    var q = new Quaternion(float.Parse(c[5], ci), float.Parse(c[6], ci), float.Parse(c[7], ci), float.Parse(c[8], ci));
                    Vector3 np;
                    if (c[1] == "1" && (p - OldSpawn).sqrMagnitude < 4f && SpawnPoint(bags.Count, out np))
                    {
                        Log.Info("sacs de couchage : " + c[0] + " ramene de l'ancien point (maison du jardin) a la maison du depart");
                        p = np; q = Quaternion.identity;
                    }
                    Bag b = Create(c[0], p, q);
                    if (c[1] == "0") ApplyFolded(b, false, false);
                }
                Log.Info("sacs de couchage : " + bags.Count + " repris de la sauvegarde");
            }
            catch (Exception e) { Log.Warn("sacs de couchage : lecture " + e.Message); }
        }

        static void Save()
        {
            if (!Session.IsHost || bags.Count == 0) return;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            foreach (Bag b in bags)
            {
                if (b.Go == null) continue;
                Vector3 p = b.Go.transform.position; Quaternion q = b.Go.transform.rotation;
                sb.Append(b.Id).Append(';').Append(b.Folded ? "1" : "0");
                foreach (float f in new[] { p.x, p.y, p.z, q.x, q.y, q.z, q.w }) sb.Append(';').Append(f.ToString("R", ci));
                sb.Append('\n');
            }
            try { File.WriteAllText(FilePath, sb.ToString()); } catch (Exception e) { Log.Warn("sacs de couchage : ecriture " + e.Message); }
        }

        // Un sac par joueur au moins : ceux qui manquent poses par terre dans la maison du debut de partie (HOMENEW, couloir
        // devant l'armoire -- capture de JD, 10/10) ; sans elle, devant l'hote.
        static bool SpawnPoint(int k, out Vector3 at)
        {
            at = Vector3.zero;
            GameObject home = Game.FindAny("HOMENEW");
            if (home == null) return false;
            at = home.transform.position + new Vector3(0.9f, 0.6f, 1.7f + k * 0.45f);   // (racine (-1285,6 ; 1,5 ; 1078,8), armoire (-1286,5 ; 1080,7))
            Vector3 g = Ground(at);
            if (g.y < at.y && at.y - g.y < 2.5f) at = g + Vector3.up * 0.25f;
            return true;
        }

        // Ancien point (0.75.18 : chambre de la maison du jardin, YARD/Building/BEDROOM1) : un sac encore plie qui y est
        // toujours est ramene au nouveau.
        static readonly Vector3 OldSpawn = new Vector3(-10.1f, -0.6f, 11.8f);

        static void EnsureCount()
        {
            int want = Mathf.Max(1, Session.RemoteCount + 1);
            if (bags.Count >= want) return;
            Transform pl = Game.PlayerT;
            if (pl == null && Game.FindAny("HOMENEW") == null) return;
            while (bags.Count < want)
            {
                int k = bags.Count;
                Vector3 at; Quaternion q = Quaternion.identity;
                bool home = SpawnPoint(k, out at);
                if (!home) { at = Ground(pl.position + pl.forward * 1.2f + pl.right * (k - 1) * 0.6f) + Vector3.up * 0.3f; q = Quaternion.Euler(0f, pl.eulerAngles.y + 90f, 0f); }
                Create("sac" + (k + 1), at, q);
                Log.Info("sacs de couchage : sac" + (k + 1) + " pose " + (home ? "dans la maison du depart" : "devant l'hote") + " en " + at.ToString("F1") + " (un par joueur)");
            }
            Save();
            SendList();
        }

        static void SendList()
        {
            if (Session.RemoteCount == 0 || bags.Count == 0) return;
            var w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@sacs").U8(bags.Count);
            foreach (Bag b in bags) w.Str(b.Id).Bool(b.Folded).Vec(b.Go.transform.position).Quat(b.Go.transform.rotation);
            Session.SendAll(w, true);
        }

        public static void OnList(NetReader r)
        {
            int n = r.U8();
            if (!Prepare()) return;
            for (int i = 0; i < n; i++)
            {
                string id = r.Str(); bool folded = r.Bool(); Vector3 p = r.Vec(); Quaternion q = r.Quat();
                Bag b = Find(id);
                if (b == null) { b = Create(id, p, q); if (!folded) ApplyFolded(b, false, false); Log.Info("sac de couchage " + id + " recu de l'hote"); }
                else if (b.Folded != folded) SetState(b, folded, p, q, false);
            }
        }

        // Sauvegarde du jeu : la liste aussi (WorldFsms / Game appellent a chaque SAVEGAME vu ici).
        public static void OnGameSave() { Save(); }

        // ---------------------------------------------------------------- essais
        // [Test] Autotest=sac : l'hote deplie sac1 a 30 s, le replie a 45 s ; chacun note l'etat a 38 et 52 s.
        static int testStep;
        static Camera testCam;
        static int shot;
        static int pickStep;
        static bool sofaPut;
        public static void Test(string mode, float t)
        {
            if (mode != "sac") return;
            // [Test] SacPrendre=1 : a 50 s l'hote lance la prise du jeu sur sac2 (comme un clic), a 53 s note ou il est.
            if (Session.IsHost && Config.GetInt("Test", "SacPrendre", 0) != 0 && bags.Count > 1)
            {
                if (t > 50f && pickStep == 0) { pickStep = 1; clickBag = bags[1]; clickFrame = Time.frameCount - 1; PickAssist(null, Time.realtimeSinceStartup); }
                if (t > 53f && pickStep == 1) { pickStep = 2; Transform p = bags[1].Go.transform.parent; Log.Info("autotest : sac, sac2 " + (p != null ? "dans la main (" + p.name + ")" : "pas pris") + ", main " + (hand != null ? hand.ActiveStateName : "?")); }
            }
            // (captures : camera d'essai qui vise le premier sac, plie a 27 s, deplie a 40 s -- dumps\sac-*.png)
            if (Session.IsHost && bags.Count > 0 && t > 20f && t < 44f)
            {
                if (testCam == null) { testCam = new GameObject("MWCoop-CameraSac").AddComponent<Camera>(); testCam.depth = 100; testCam.nearClipPlane = 0.03f; testCam.fieldOfView = 55f; }
                Transform bt = bags[0].Go.transform;
                float d = 1.1f;
                testCam.transform.position = bags[0].Folded ? bt.position + bt.right * d * 0.8f + Vector3.up * d * 0.75f - bt.forward * d * 0.5f
                                                            : bt.position + Vector3.up * 1.75f - bt.forward * 1.1f + bt.right * 0.35f;   // (deplie : d'en haut, piece etroite)
                testCam.transform.LookAt(bt.position + Vector3.up * 0.1f);
                if (t > 27f && shot == 0 || t > 40f && shot == 1)
                {
                    shot++;
                    string png = Path.Combine(Path.Combine(Log.DataDir, "dumps"), shot == 1 ? "sac-plie.png" : "sac-deplie.png");
                    Directory.CreateDirectory(Path.GetDirectoryName(png));
                    Application.CaptureScreenshot(png);
                    Log.Info("autotest : capture " + png);
                }
            }
            if (t > 44f && testCam != null) { UnityEngine.Object.Destroy(testCam.gameObject); testCam = null; }
            if (Session.IsHost && bags.Count > 0)
            {
                Bag b = bags[0];
                // [Test] SacCanape=1 : sac plie pose sur le canape du jeu a 24 s (deplie dessus a 30 s ; Vaydon, 0.79.2 : « a l'envers »).
                if (Config.GetInt("Test", "SacCanape", 0) != 0 && t > 24f && !sofaPut)
                {
                    sofaPut = true;
                    GameObject sofa = Game.FindAny("EQUIPMENTS/sofa(itemx)");
                    if (sofa != null) { b.Go.transform.position = sofa.transform.position + Vector3.up * 0.9f; if (b.Rb != null) b.Rb.velocity = Vector3.zero; }
                    Log.Info("autotest : sac sur le canape " + (sofa != null ? sofa.transform.position.ToString("F2") : "introuvable"));
                }
                if (t > 30f && testStep == 0) { testStep = 1; Transform pl = Game.PlayerT; Log.Info("autotest : sac, sol plat " + Flat(Ground(b.Go.transform.position), Quaternion.Euler(0f, pl != null ? pl.eulerAngles.y + 90f : 0f, 0f)) + ", coins" + flatInfo + " (le joueur ne pourrait le deplier que si vrai)"); SetState(b, false, Ground(b.Go.transform.position), Quaternion.Euler(0f, pl != null ? pl.eulerAngles.y + 90f : 0f, 0f), true); Log.Info("autotest : sac deplie, haut du sac " + b.Go.transform.up.ToString("F2") + ", modele " + (b.Mf != null ? b.Mf.transform.up.ToString("F2") : "?")); }
                // [Test] SacDormir=1 : l'hote s'endort sur le sac deplie a 32 s (fatigue posee, etat « Get positions » du lit),
                // il reste deplie ; position du joueur notee.
                if (Config.GetInt("Test", "SacDormir", 0) != 0)
                {
                    if (t > 32f && testStep == 2 && b.Fsm != null)
                    {
                        testStep = 5;
                        FsmFloat fat = FsmVariables.GlobalVariables.FindFsmFloat("PlayerFatigue"); if (fat != null) fat.Value = 60f;
                        Game.SetState(b.Fsm, "Get positions");
                        Log.Info("autotest : sac, au lit (" + b.Fsm.ActiveStateName + ")");
                    }
                    if (testStep == 5 && t > 34f && Time.frameCount % 120 == 0)
                    {
                        Transform pl = Game.PlayerT;
                        Log.Info("autotest : sac, lit " + b.Fsm.ActiveStateName + ", dort " + Game.GlobalBool("PlayerSleeps") + ", joueur " + (pl != null ? b.Go.transform.InverseTransformPoint(pl.position).ToString("F2") + " parent " + (pl.parent != null ? pl.parent.name : "-") : "?")
                                 + ", camera " + (Camera.main != null ? b.Go.transform.InverseTransformPoint(Camera.main.transform.position).ToString("F2") : "?")
                                 + ", pivot " + b.Go.transform.InverseTransformPoint(b.Sleep.Find("AnimPivotSleep").position).ToString("F2") + " rot " + (Quaternion.Inverse(b.Go.transform.rotation) * b.Sleep.Find("AnimPivotSleep").rotation).eulerAngles.ToString("F0"));
                    }
                }
                else if (t > 45f && testStep == 2) { testStep = 3; SetState(b, true, b.Go.transform.position + Vector3.up * 0.25f, b.Go.transform.rotation, true); }
            }
            if (t > 38f && (testStep == 1 || !Session.IsHost && testStep == 0))
            {
                testStep = 2; Log.Info("autotest : sacs " + Describe());
                Camera mc = Camera.main;
                if (mc != null) Log.Info("autotest : sacs, camera du joueur " + mc.name + " : couches 0 " + ((mc.cullingMask & 1) != 0) + ", 2 " + ((mc.cullingMask & 4) != 0) + ", 19 " + ((mc.cullingMask & (1 << 19)) != 0));
            }
            if (t > 52f && testStep == 3 || !Session.IsHost && t > 60f && testStep == 2) { testStep = 4; Log.Info("autotest : sacs " + Describe()); }
        }

        public static string Describe()
        {
            var sb = new System.Text.StringBuilder(bags.Count + " :");
            foreach (Bag b in bags) if (b.Go != null) sb.Append(' ').Append(b.Id).Append(b.Folded ? " plie" : " deplie").Append(" en ").Append(b.Go.transform.position.ToString("F1")).Append(b.Fsm != null ? " lit " + b.Fsm.ActiveStateName : "");
            return sb.ToString();
        }

        // Essais (captures) : premier sac et sa pose.
        public static Transform First { get { return bags.Count > 0 && bags[0].Go != null ? bags[0].Go.transform : null; } }
    }
}
