using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Corps visible d'un joueur distant : copie du personnage 'Char' d'un marcheur du jeu
    // (HUMANS/Randomizer/Walkers/*/Pivot/Char : bodymesh + skeleton anime), debarrasse de sa
    // logique, avec le materiau (« apparence ») choisi par le joueur.
    // Animations (clips du jeu, squelette « fat » des PNJ), en couches :
    //  - corps (Animation de 'skeleton') : fat_walk / fat_standing / assis (worker1_sitdown tenu) ;
    //  - bras : comme chez les PNJ, une 2e Animation sur l'os de l'epaule (collar_right/collar_left),
    //    dont les clips partent de cet os : porter, boire, saluer (droit), fumer (gauche).
    //  - apres les animations (LatePose) : au volant, la pose d'un conducteur de la circulation
    //    (TRAFFIC/.../Driver, squelette fige ; voiture ou camion), la tete tournee comme la camera du
    //    joueur, meme a 360 degres ; a pied, le buste et la tete suivent le regard (se penche en
    //    regardant en bas, en arriere en regardant en haut) ; au repos, les bras le long du corps.
    //  - vetements portes (Wear, bits de l'etat du joueur) : teinte du corps (veste, combinaison), copie du
    //    casque sur la tete.
    //  - gestes (Gestures) : seuls les clips fat_* et worker1_sitdown animent bien ce squelette (ceux des autres PNJ
    //    ne bougent que l'os racine) -> poses calculees apres les animations : bras vers une cible (IK a deux os :
    //    coup, doigt, pouce, montre, pousser, uriner, objet tenu), buste penche (touche de penche, ivresse qui
    //    balance), corps a terre (evanoui, assomme), assis sur le siege envoye (chaise, canape, banc), jet copie de
    //    celui du joueur local.
    public class Avatar
    {
        static GameObject template;          // copie inactive, sans logique
        static Vector3 charOffset;           // position de Char par rapport aux pieds du marcheur
        static Quaternion charRotation = Quaternion.identity;
        static Vector3 charScale = Vector3.one;    // echelle globale de Char (ses parents sont mis a l'echelle)
        static Dictionary<string, Material> materials;
        static Dictionary<string, AnimationClip> clips;

        // Nom dans l'avatar -> clip du jeu, os de depart (null = tout le corps), mode.
        static readonly string[,] Clips =
        {
            { "assis",   "worker1_sitdown",        "",             "clamp" },
            { "porter",  "fat_handsright_carry",   "collar_right", "clamp" },
            { "boire",   "fat_handsright_drink",   "collar_right", "loop"  },
            { "saluer",  "fat_waving_hello",       "collar_right", "once"  },
            { "fumer",   "fat_handsleft_smoke",    "collar_left",  "loop"  },
            // Balancement des bras en marchant (le clip du corps n'anime pas les bras, comme chez les PNJ).
            { "marche_d","fat_handsright_walk",    "collar_right", "loop"  },
            { "marche_g","fat_handsleft_walk",     "collar_left",  "loop"  },
        };

        public GameObject Root;
        public PlayerInfo Player;
        Animation anim;
        SkinnedMeshRenderer body;
        string skin;
        Vector3 pos;
        float yaw;
        bool placed, wasHello;
        float nextDiag;
        string body0;
        Animation armR, armL;
        Dictionary<string, Transform> bones;
        bool inCar, moving, sitting;
        Vector3 seatAnchor;                  // tete au repos sur le siege (repere voiture)
        bool anchorSet;
        Vector3 leanOff;                     // ecart camera - ancre (repere voiture) : se pencher
        Vector3 eyesRest = new Vector3(0f, 1.2f, 0.1f);   // yeux / avatar, pose de conduite sans penche
        const float SleepEyes = 1.55f;
        float sleepDiag;                     // yeux / pieds, debout (pose couchee du dormeur)
        string anchorCar;
        bool passenger;                      // assis a une place passager (pose assise, pas de volant)
        string carName;
        float armWR, armWL, crouchW, crouchDepth;
        bool crouching;
        // Gestes (Gestures) : a terre (evanoui, assomme), penche et ivresse affiches, poids des bras vers leur cible,
        // assis sur un siege du jeu (bassin mesure dans la pose assise, repere avatar), jet.
        float lieW, sideS, fwdS, swayT;
        readonly float[] wR = new float[5], wL = new float[4];
        Vector3 sitPelvis, chairSeatAt;
        float chairTop = 0.45f;
        bool chair, sitMeasured, peeTried;
        GameObject pee;
        Quaternion peeLocal = Quaternion.identity;
        string gestR = "", gestL = "";
        // Os que l'animation en cours ne pilote pas : nos retouches s'y ajouteraient d'une image a
        // l'autre. On garde la valeur de base et celle posee ; si l'os n'a pas bouge depuis, on le remet.
        Quaternion headRest;                 // tete par rapport a l'avatar, debout (pour viser en voiture)
        bool headRestSet;
        Transform charT;
        int facingFrames;            // images animees vues avant de mesurer le sens du modele
        float nextPoseLog;
        Transform[] boneList;
        Quaternion[] baseRot, setRot;
        Vector3[] basePos, setPos;
        static Dictionary<string, Quaternion> poseCar, poseTruck;
        Quaternion skelRot;                  // pose de repos de l'os racine (skeleton)
        Vector3 skelPos;
        public static string ForceClip;      // essais : clip du jeu tenu a ForceTime (reperage des poses)
        public static float ForceTime;
        static bool posesBuilt;

        // Yeux par rapport a l'os 'head' (devant et au-dessus), pour caler la tete sur la camera.
        static readonly Vector3 EyeOffset = new Vector3(0f, 0.08f, 0.1f);
        Transform headBone;

        public Vector3 HeadPosition
        {
            get { return headBone != null ? headBone.position + Vector3.up * 0.35f : Root.transform.position + Vector3.up * 1.95f; }
        }

        public static void ResetTemplate()
        {
            if (template != null) Object.Destroy(template);
            template = null;
            materials = null;
            clips = null;
        }

        static bool BuildTemplate()
        {
            if (template != null) return true;
            GameObject walkers = Game.FindAny("HUMANS/Randomizer/Walkers");
            if (walkers == null) return false;
            foreach (Transform w in walkers.transform)
            {
                Transform ch = w.Find("Pivot/Char");
                if (ch == null || ch.Find("bodymesh") == null || ch.Find("skeleton") == null) continue;
                charOffset = Quaternion.Inverse(w.rotation) * (ch.position - w.position);
                charRotation = Quaternion.Inverse(w.rotation) * ch.rotation;
                charScale = ch.lossyScale;
                template = (GameObject)Object.Instantiate(ch.gameObject);
                template.name = "MWCoop-AvatarModele";
                template.SetActive(false);
                Strip(template);
                Log.Info("modele d'avatar : " + Recon.Path(ch) + ", decalage " + charOffset + ", echelle " + charScale);
                return true;
            }
            return false;
        }

        // Ne garde que l'affichage et l'animation.
        static void Strip(GameObject go)
        {
            foreach (MonoBehaviour m in go.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(m);
            foreach (Joint j in go.GetComponentsInChildren<Joint>(true)) Object.DestroyImmediate(j);
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (Rigidbody r in go.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(r);
            foreach (AudioSource a in go.GetComponentsInChildren<AudioSource>(true)) Object.DestroyImmediate(a);
        }

        // Matiere du jeu par son nom. Jamais une matiere sans nom : celles creees en cours de partie (effets d'image des
        // cameras, cigarette de l'avatar) n'en ont pas, et un nom vide (reglage absent) en aurait pris une au hasard.
        public static Material FindMaterial(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (materials == null)
            {
                materials = new Dictionary<string, Material>();
                foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(Material)))
                    if (o.name.Length > 0 && !materials.ContainsKey(o.name)) materials[o.name] = (Material)o;
            }
            Material m;
            return materials.TryGetValue(name, out m) ? m : null;
        }

        // Apparences proposees : materiaux des corps des PNJ (char_shirtNN, cop_shirt...).
        public static List<string> SkinNames()
        {
            var set = new SortedDictionary<string, bool>();
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(SkinnedMeshRenderer)))
            {
                var smr = (SkinnedMeshRenderer)o;
                if (smr.sharedMesh == null || smr.sharedMesh.name != "bodymesh" || smr.sharedMaterial == null) continue;
                string n = smr.sharedMaterial.name;
                if (n.Contains("(Instance)") || n.Contains("ghost")) continue;
                set[n] = true;
            }
            return new List<string>(set.Keys);
        }

        public static Avatar Create(PlayerInfo pi)
        {
            if (!BuildTemplate()) return null;
            var a = new Avatar { Player = pi };
            a.Build("MWCoop-Joueur-" + pi.Id);
            Log.Info("avatar cree pour " + pi.Name + " (#" + pi.Id + ")");
            return a;
        }

        // Copie du modele sous une nouvelle racine 'name', animations pretes.
        void Build(string name)
        {
            Root = new GameObject(name);
            GameObject ch = (GameObject)Object.Instantiate(template);
            ch.name = "Char";
            ch.transform.parent = Root.transform;
            ch.transform.localPosition = charOffset;
            ch.transform.localRotation = charRotation;
            ch.transform.localScale = charScale;
            ch.SetActive(true);
            anim = ch.GetComponentInChildren<Animation>();
            if (anim != null)
            {
                anim.cullingType = AnimationCullingType.AlwaysAnimate;
                skelRot = anim.transform.localRotation;
                skelPos = anim.transform.localPosition;
                headBone = FindBone(anim.transform, "head");
                bones = new Dictionary<string, Transform>();
                foreach (Transform b in anim.GetComponentsInChildren<Transform>(true)) if (!bones.ContainsKey(b.name)) bones[b.name] = b;
                AddClips();
            }
            charT = ch.transform;
            body = ch.GetComponentInChildren<SkinnedMeshRenderer>();
            if (body != null) { body.updateWhenOffscreen = true; body.enabled = true; }
        }

        // ---------------------------------------------------------------- apercu des tenues (Studio)
        // Personnage seul, sans joueur ni reseau : animations arretees, la pose debout au repos n'est posee que par
        // PoseStanding (echantillons des clips, puis bras le long du corps comme Pose), tourne pour regarder l'avant
        // de sa racine (comme FixFacing), tout sur la couche 'layer'.
        Transform[] restBones;
        Quaternion[] restRot;
        Vector3[] restPos;

        public static Avatar CreatePreview(string name, int layer)
        {
            if (!BuildTemplate()) return null;
            var a = new Avatar();
            a.Build(name);
            if (a.anim == null || a.body == null || a.bones == null) { a.Destroy(); return null; }
            a.anim.playAutomatically = false;
            a.anim.Stop();
            if (a.armR != null) a.armR.Stop();
            if (a.armL != null) a.armL.Stop();
            if (a.anim["fat_standing"] == null) { AnimationClip c = FindClip("fat_standing"); if (c != null) a.anim.AddClip(c, "fat_standing"); }
            foreach (Transform t in a.Root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            a.restBones = new List<Transform>(a.bones.Values).ToArray();
            a.restRot = new Quaternion[a.restBones.Length];
            a.restPos = new Vector3[a.restBones.Length];
            for (int i = 0; i < a.restBones.Length; i++) { a.restRot[i] = a.restBones[i].localRotation; a.restPos[i] = a.restBones[i].localPosition; }
            a.PoseStanding(0f);
            float ang;
            if (a.FacingAngle(out ang)) a.charT.localRotation = Quaternion.Euler(0f, -ang, 0f) * a.charT.localRotation;
            return a;
        }

        // Debout au repos, a l'instant 'time' (s) du clip : meme pose que l'avatar immobile sans geste.
        public void PoseStanding(float time)
        {
            if (anim == null || restBones == null) return;
            for (int i = 0; i < restBones.Length; i++)
                if (restBones[i] != null) { restBones[i].localRotation = restRot[i]; restBones[i].localPosition = restPos[i]; }
            SampleClip(anim, "fat_standing", time, false);
            // Bras : le balancement de marche fige au quart du cycle (ArmPlay a l'arret).
            SampleClip(armR, "marche_d", 0.25f, true);
            SampleClip(armL, "marche_g", 0.25f, true);
            anim.transform.localRotation = skelRot;
            anim.transform.localPosition = skelPos;
            ArmDown("shoulder_right", "hand_right", 1f, 1f);
            ArmDown("shoulder_left", "hand_left", -1f, 1f);
        }

        static void SampleClip(Animation a, string clip, float t, bool normalized)
        {
            if (a == null) return;
            AnimationState s = a[clip];
            if (s == null) return;
            s.enabled = true;
            s.weight = 1f;
            if (normalized) s.normalizedTime = t; else s.time = s.length > 0f ? t % s.length : 0f;
            a.Sample();
            s.enabled = false;
        }

        Material previewDefault;

        // Tenue de l'apercu ; inconnue ou vide : la matiere d'origine du modele.
        public void PreviewSkin(string s)
        {
            if (body == null) return;
            if (previewDefault == null) previewDefault = body.sharedMaterial;
            skin = s;
            clothFlags = 0;
            if (FindMaterial(s) == null) { baseMat = null; body.sharedMaterial = previewDefault; return; }
            ApplyMaterial();
        }

        public Bounds BodyBounds { get { return body != null ? body.bounds : new Bounds(Root.transform.position, Vector3.zero); } }
        public Transform HeadBone { get { return headBone; } }

        static AnimationClip FindClip(string name)
        {
            if (clips == null)
            {
                clips = new Dictionary<string, AnimationClip>();
                foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(AnimationClip)))
                    if (!clips.ContainsKey(o.name)) clips[o.name] = (AnimationClip)o;
            }
            AnimationClip c;
            return clips.TryGetValue(name, out c) ? c : null;
        }

        static Transform FindBone(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { Transform r = FindBone(c, name); if (r != null) return r; }
            return null;
        }

        Animation ArmAnim(string bone)
        {
            Transform b = FindBone(anim.transform, bone);
            if (b == null) return null;
            Animation a = b.GetComponent<Animation>() ?? b.gameObject.AddComponent<Animation>();
            a.playAutomatically = false;
            a.cullingType = AnimationCullingType.AlwaysAnimate;
            return a;
        }

        void AddClips()
        {
            var missing = new List<string>();
            armR = ArmAnim("collar_right");
            armL = ArmAnim("collar_left");

            for (int i = 0; i < Clips.GetLength(0); i++)
            {
                AnimationClip c = FindClip(Clips[i, 1]);
                if (c == null) { missing.Add(Clips[i, 1]); continue; }
                Animation target = Clips[i, 2] == "collar_right" ? armR : Clips[i, 2] == "collar_left" ? armL : anim;
                if (target == null) { missing.Add(Clips[i, 2]); continue; }
                target.AddClip(c, Clips[i, 0]);
                target[Clips[i, 0]].wrapMode = Clips[i, 3] == "loop" ? WrapMode.Loop : Clips[i, 3] == "once" ? WrapMode.Once : WrapMode.ClampForever;
            }
            if (missing.Count > 0) Log.Warn("avatar : absents " + string.Join(", ", missing.ToArray()));
        }

        // Poses de conduite : squelettes figes des conducteurs de la circulation (voiture / camion).
        static void BuildDriverPoses()
        {
            posesBuilt = true;
            // Conducteurs de la circulation d'abord (assis normalement), pas ceux des courses (couches).
            var found = new List<KeyValuePair<int, Transform>>();
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(Transform)))
            {
                var t = (Transform)o;
                if (t.name != "Driver" || t.gameObject.hideFlags != HideFlags.None) continue;
                Transform pelvis = FindBone(t, "pelvis");
                if (pelvis == null || pelvis.parent == null) continue;
                string path = Recon.Path(t);
                found.Add(new KeyValuePair<int, Transform>(path.StartsWith("TRAFFIC") ? 0 : path.Contains("RACE") ? 2 : 1, t));
            }
            found.Sort((a, b) => a.Key.CompareTo(b.Key));
            foreach (KeyValuePair<int, Transform> kv in found)
            {
                Transform t = kv.Value, sk = FindBone(t, "pelvis").parent;
                bool truck = t.parent != null && (FindBone(t.parent, "TruckSteeringPivot") != null || FindBone(t.parent, "IKpivot") != null);
                if (truck ? poseTruck != null : poseCar != null) continue;
                var d = new Dictionary<string, Quaternion>();
                foreach (Transform b in sk.GetComponentsInChildren<Transform>(true))
                    if (b != sk && b.name != "HeadPivot" && b.name != "head") d[b.name] = b.localRotation;
                if (truck) poseTruck = d; else poseCar = d;
                Log.Info("avatar : pose de conduite " + (truck ? "camion" : "voiture") + " prise sur " + Recon.Path(t));
            }
        }

        static Dictionary<string, Quaternion> DriverPose(string car)
        {
            if (!posesBuilt) BuildDriverPoses();
            bool big = car != null && (car.StartsWith("GIFU") || car.StartsWith("KEKMET") || car.StartsWith("BACHGLOTZ"));
            if (big && poseTruck != null) return poseTruck;
            return poseCar ?? poseTruck;
        }

        Transform Bone(string n) { Transform t; return bones != null && bones.TryGetValue(n, out t) ? t : null; }

        // Rotation monde autour d'axes de l'avatar, ajoutee a la pose courante de l'os.
        void Turn(Transform b, float yaw, float pitch)
        {
            if (b == null) return;
            Transform r = Root.transform;
            b.rotation = Quaternion.AngleAxis(yaw, r.up) * Quaternion.AngleAxis(pitch, r.right) * b.rotation;
        }

        // Bras tendu vers le bas (le long du corps), dose par w.
        void ArmDown(string shoulder, string hand, float side, float w)
        {
            Transform s = Bone(shoulder), h = Bone(hand);
            if (s == null || h == null || w <= 0.001f) return;
            Transform r = Root.transform;
            Vector3 dir = h.position - s.position;
            Vector3 target = (-r.up + r.right * side * 0.12f + r.forward * 0.04f).normalized;
            s.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(dir, target), w) * s.rotation;
        }

        // Accroupi d'apres la profondeur w (0 debout, 0,5 accroupi, 1 au ras du sol), en suivant la camera :
        //  0 -> 0,5 : accroupi pour de vrai : bassin bas et en arriere, genoux en avant, pieds a plat ou
        //            ils sont (IK : ils suivent aussi la marche), buste un peu penche ;
        //  0,5 -> 1 : second niveau du jeu (pour regarder dessous) : a genoux, penche en avant -- genoux au
        //            sol sous le bassin, tibias a plat derriere, buste presque a l'horizontale, bras tombant
        //            vers le sol. La tete compense pour regarder devant (le regard s'y ajoute ensuite).
        void Crouch(float w)
        {
            Transform pelvis = Bone("pelvis");
            if (pelvis == null) return;
            Transform r = Root.transform;
            float a = Mathf.Clamp01(w / 0.5f), b = Mathf.Clamp01((w - 0.5f) / 0.5f);
            Transform al = Bone("ankle_left"), ar = Bone("ankle_right");
            Vector3 footL = al != null ? r.InverseTransformPoint(al.position) : Vector3.zero;
            Vector3 footR = ar != null ? r.InverseTransformPoint(ar.position) : Vector3.zero;
            Vector3 p0 = r.InverseTransformPoint(pelvis.position);
            Vector3 squat = p0 + new Vector3(0f, -0.50f * a, -0.16f * a);
            Vector3 kneel = new Vector3(p0.x, 0.50f, p0.z - 0.02f);
            Vector3 pT = Vector3.Lerp(squat, kneel, b);
            // Les jambes ne sont pas des enfants du bassin dans ce squelette : c'est tout le squelette qui
            // descend (il est remis a sa place de repos a chaque image), l'IK replie ensuite les jambes.
            anim.transform.position += r.TransformVector(pT - p0);
            if (al != null) Leg("thig_left", "knee_left", "ankle_left", r.TransformPoint(Vector3.Lerp(footL, new Vector3(footL.x, 0.1f, pT.z - 0.42f), b)));
            if (ar != null) Leg("thig_right", "knee_right", "ankle_right", r.TransformPoint(Vector3.Lerp(footR, new Vector3(footR.x, 0.1f, pT.z - 0.42f), b)));
            float lean = 20f * a + 58f * b;
            Turn(Bone("spine_middle"), 0f, lean * 0.55f);
            Turn(Bone("spine_upper"), 0f, lean * 0.45f);
            Turn(Bone("HeadPivot") ?? headBone, 0f, -lean * 0.85f);
        }

        // IK a deux os : la hanche ne bouge pas, la cheville revient sur 'foot', genou vers l'avant.
        void Leg(string hip, string knee, string ankle, Vector3 foot)
        {
            Transform h = Bone(hip), k = Bone(knee), a = Bone(ankle);
            if (h == null || k == null || a == null || foot == Vector3.zero) return;
            float l1 = (k.position - h.position).magnitude, l2 = (a.position - k.position).magnitude;
            Vector3 d = foot - h.position;
            float dist = Mathf.Clamp(d.magnitude, Mathf.Abs(l1 - l2) + 0.01f, l1 + l2 - 0.001f);
            Vector3 dir = d.normalized;
            Vector3 bend = Vector3.ProjectOnPlane(Root.transform.forward, dir).normalized;
            float cosA = Mathf.Clamp((l1 * l1 + dist * dist - l2 * l2) / (2f * l1 * dist), -1f, 1f);
            Vector3 kneePos = h.position + dir * (l1 * cosA) + bend * (l1 * Mathf.Sqrt(1f - cosA * cosA));
            h.rotation = Quaternion.FromToRotation(k.position - h.position, kneePos - h.position) * h.rotation;
            k.rotation = Quaternion.FromToRotation(a.position - k.position, foot - k.position) * k.rotation;
        }

        public void LatePose()
        {
            if (anim == null || bones == null || Root == null) return;
            if (boneList == null)
            {
                boneList = new List<Transform>(bones.Values).ToArray();
                baseRot = new Quaternion[boneList.Length]; setRot = new Quaternion[boneList.Length];
                basePos = new Vector3[boneList.Length]; setPos = new Vector3[boneList.Length];
                for (int i = 0; i < boneList.Length; i++) { setRot[i] = baseRot[i] = boneList[i].localRotation; setPos[i] = basePos[i] = boneList[i].localPosition; }
            }
            for (int i = 0; i < boneList.Length; i++)
            {
                Transform b = boneList[i];
                if (b == null) continue;
                if (b.localRotation == setRot[i]) b.localRotation = baseRot[i];
                if (b.localPosition == setPos[i]) b.localPosition = basePos[i];
                baseRot[i] = b.localRotation;
                basePos[i] = b.localPosition;
            }
            // Os racine (skeleton) : certains clips le tournent (assis : worker1_sitdown) et les autres ne
            // le remettent jamais droit -> apres s'etre assis une fois, l'avatar restait tourne. Toujours
            // a sa pose de repos.
            if (ForceClip == null || Config.GetInt("Test", "RacineRepos", 1) != 0)
            {
                anim.transform.localRotation = skelRot;
                anim.transform.localPosition = skelPos;
            }
            FixFacing();
            Helmet((Player.State.Flags & PlayerSync.F_Helmet) != 0);
            Pose();
            PlaceCigarette();
            PlaceDrink();
            PlacePee();
            // Assis sur un siege : ou tombe le bassin dans la pose assise (repere avatar) -> Apply y place le siege.
            Transform pv = Bone("pelvis");
            if (chair && sitting && pv != null)
            {
                Vector3 pl = Root.transform.InverseTransformPoint(pv.position);
                sitPelvis = sitMeasured ? Vector3.Lerp(sitPelvis, pl, 0.2f) : pl;
                sitMeasured = true;
            }
            if (Config.GetInt("Test", "JournalPose", 0) != 0 && Time.realtimeSinceStartup >= nextPoseLog && headBone != null && Bone("pelvis") != null)
            {
                nextPoseLog = Time.realtimeSinceStartup + 5f;
                Transform r = Root.transform;
                Vector3 h = r.InverseTransformPoint(headBone.position) - r.InverseTransformPoint(Bone("pelvis").position);
                Vector3 fwd = r.InverseTransformDirection(headBone.forward);
                Log.Info("pose " + Player.Name + " : regard " + Player.State.Pitch.ToString("F0") + ", tete/bassin " + h.ToString("F2") + ", axe tete " + fwd.ToString("F2"));
                var sb = new System.Text.StringBuilder("os de " + Player.Name + " (repere avatar, yaw " + r.eulerAngles.y.ToString("F0") + ") :");
                foreach (string n in new[] { "pelvis", "shoulder_left", "shoulder_right", "hand_left", "hand_right", "knee_left", "knee_right", "ankle_left", "ankle_right", "head" })
                {
                    Transform bt = Bone(n);
                    if (bt != null) sb.Append(' ').Append(n).Append(r.InverseTransformPoint(bt.position).ToString("F2"));
                }
                if (cig != null && cig.activeSelf)
                {
                    MeshRenderer cr = cig.GetComponentInChildren<MeshRenderer>();
                    Vector3 cw = cr != null ? cr.bounds.center : cig.transform.position;
                    sb.Append(" cigarette ").Append(r.InverseTransformPoint(cw).ToString("F2"));
                    Camera cam = PlayerSync.LocalCamera != null ? PlayerSync.LocalCamera.GetComponent<Camera>() : Camera.main;
                    if (cam != null && Bone("hand_left") != null)
                        sb.Append(" ecran main ").Append(cam.WorldToScreenPoint(Bone("hand_left").position).ToString("F0")).Append(" doigts ").Append(Bone("finger_left") != null ? cam.WorldToScreenPoint(Bone("finger_left").position).ToString("F0") : "-")
                          .Append(" cigarette ").Append(cam.WorldToScreenPoint(cw).ToString("F0")).Append(" nb rendus ").Append(cig.GetComponentsInChildren<Renderer>(true).Length);
                }
                if (charT != null) sb.Append(" Char yaw local ").Append(charT.localEulerAngles.ToString("F0")).Append(" skeleton ").Append(anim.transform.localEulerAngles.ToString("F0"));
                Log.Info(sb.ToString());
            }
            for (int i = 0; i < boneList.Length; i++)
            {
                Transform b = boneList[i];
                if (b == null) continue;
                setRot[i] = b.localRotation;
                setPos[i] = b.localPosition;
            }
        }

        // Les clips des PNJ tournent l'os racine : le modele anime ne regarde pas l'avant de sa racine.
        // Apres quelques images animees, debout, on mesure son sens (epaule gauche -> epaule droite) et
        // on tourne le modele pour qu'il regarde la ou regarde le joueur (une fois).
        void FixFacing()
        {
            if (facingFrames < 0 || charT == null || sitting || inCar || lieW > 0.01f) return;
            if (++facingFrames < 10) return;
            facingFrames = -1;
            float ang;
            if (!FacingAngle(out ang)) return;
            charT.localRotation = Quaternion.Euler(0f, -ang, 0f) * charT.localRotation;
            headRestSet = false;   // la tete au repos se reprend dans le bon sens
            Log.Info("avatar " + Player.Name + " : modele tourne de " + (-ang).ToString("F0") + " deg pour regarder devant");
        }

        // Sens du modele (epaule gauche -> epaule droite) par rapport a l'avant de la racine, en degres.
        bool FacingAngle(out float ang)
        {
            ang = 0f;
            Transform sr = Bone("shoulder_right"), sl = Bone("shoulder_left");
            if (sr == null || sl == null) return false;
            Vector3 right = Root.transform.InverseTransformDirection(sr.position - sl.position);
            right.y = 0f;
            if (right.sqrMagnitude < 1e-4f) return false;
            Vector3 fwd = Vector3.Cross(right.normalized, Vector3.up);
            ang = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            return true;
        }

        void Pose()
        {
            PlayerState st = Player.State;
            int f = st.Flags;
            float pitch = Mathf.Clamp(st.Pitch, -80f, 80f);
            if (!headRestSet && Root != null)
            {
                // Tete au repos par rapport a l'avatar : prise sur le modele debout (premiere image).
                Transform hp0 = Bone("HeadPivot") ?? headBone;
                if (hp0 != null) { headRest = Quaternion.Inverse(Root.transform.rotation) * hp0.rotation; headRestSet = true; }
            }
            if (inCar || Config.GetInt("Test", "TestPoseConduite", 0) != 0)
            {
                // Passager : jambes allongees et buste du conducteur, mais pas ses bras (pas de volant).
                Dictionary<string, Quaternion> pose = DriverPose(passenger ? "voiture" : carName);
                if (pose != null)
                    foreach (KeyValuePair<string, Quaternion> kv in pose)
                    {
                        if (passenger && (kv.Key.Contains("collar") || kv.Key.Contains("shoulder") || kv.Key.Contains("arm") || kv.Key.Contains("hand") || kv.Key.Contains("finger"))) continue;
                        Transform b = Bone(kv.Key); if (b != null) b.localRotation = kv.Value;
                    }
                if (passenger)
                {
                    // Bras poses sur les cuisses.
                    ArmDown("shoulder_right", "hand_right", 1f, 1f);
                    ArmDown("shoulder_left", "hand_left", -1f, 1f);
                }
                // Yeux au repos (pose de conduite, sans penche) : servent a placer le corps sur le siege.
                if (headBone != null) eyesRest = Quaternion.Inverse(Root.transform.rotation) * (headBone.position - Root.transform.position) + EyeOffset;
                // Se pencher : le buste va vers la camera (cote : autour de l'avant, avant : autour de la droite).
                float side = Mathf.Atan2(leanOff.x, 0.55f) * Mathf.Rad2Deg, fwdLean = Mathf.Atan2(leanOff.z, 0.55f) * Mathf.Rad2Deg;
                foreach (string sp in new[] { "spine_middle", "spine_upper" })
                {
                    Transform b = Bone(sp);
                    if (b == null) continue;
                    b.rotation = Quaternion.AngleAxis(-side * 0.5f, Root.transform.forward) * Quaternion.AngleAxis(fwdLean * 0.5f, Root.transform.right) * b.rotation;
                }
                // Tete : regard relatif a la voiture, sans limite (tour complet accepte), quelle que soit
                // l'inclinaison du dossier.
                Transform hp = Bone("HeadPivot") ?? headBone;
                if (hp != null && headRestSet)
                    hp.rotation = Root.transform.rotation * Quaternion.Euler(pitch, Mathf.DeltaAngle(Root.transform.eulerAngles.y, st.Yaw), 0f) * headRest;
                return;
            }
            if (ForceClip != null) return;
            if (lieW > 0.01f)
            {
                // A terre : raide, bras le long du corps (vers les pieds).
                gestR = gestL = "a terre";
                ArmDown("shoulder_right", "hand_right", 1f, 1f);
                ArmDown("shoulder_left", "hand_left", -1f, 1f);
                return;
            }
            if ((f & PlayerSync.F_Sleep) != 0) return;
            // Accroupi (deux niveaux, comme le jeu) : voir Crouch.
            crouchW = Mathf.MoveTowards(crouchW, crouchDepth, Time.deltaTime * 3f);
            if (crouchW > 0.001f) Crouch(crouchW);
            // Buste et tete suivent le regard : penche en avant en regardant en bas, en arriere en haut.
            Turn(Bone("spine_middle"), 0f, pitch * 0.15f);
            Turn(Bone("spine_upper"), 0f, pitch * 0.2f);
            Turn(Bone("HeadPivot") ?? headBone, 0f, pitch * 0.5f);
            Gestures.Remote g = Gestures.Of(Player.Id);
            LeanPose(st, g);
            ChooseGestures(f, g);
            // Au repos et sans geste : bras le long du corps.
            bool idle = !moving && (!sitting || crouching);   // accroupi : bras le long du corps / vers le sol
            float dt = Time.deltaTime * 3f;
            armWR = Mathf.MoveTowards(armWR, idle && gestR.Length == 0 && (f & (PlayerSync.F_Drink | PlayerSync.F_Carry | PlayerSync.F_Hello)) == 0 && (armR == null || !armR.IsPlaying("saluer")) ? 1f : 0f, dt);
            armWL = Mathf.MoveTowards(armWL, idle && gestL.Length == 0 && (f & PlayerSync.F_Smoke) == 0 ? 1f : 0f, dt);
            ArmDown("shoulder_right", "hand_right", 1f, armWR);
            ArmDown("shoulder_left", "hand_left", -1f, armWL);
            GesturePose(st, f, g);
        }

        // ---------------------------------------------------------------- gestes (Gestures)
        // Buste penche : touche de penche de l'autre (degres, droite +, avant +), balancement d'ivresse (PlayerDrunk :
        // jusqu'a 7 deg a 2,5), la tete se redresse a moitie. Assis sur un siege : la tete et le haut du buste suivent
        // son regard (le corps garde le sens du siege).
        void LeanPose(PlayerState st, Gestures.Remote g)
        {
            Transform r = Root.transform;
            float side = 0f, fwd = 0f, drunk = 0f;
            if (g != null && !chair && !crouching) { side = g.LeanSide; fwd = g.LeanFwd; }
            if (g != null) drunk = g.Drunk;
            sideS = Mathf.MoveTowards(sideS, Mathf.Clamp(side, -30f, 30f), Time.deltaTime * 90f);
            fwdS = Mathf.MoveTowards(fwdS, Mathf.Clamp(fwd, -30f, 40f), Time.deltaTime * 90f);
            swayT += Time.deltaTime;
            float amp = Mathf.Clamp01(drunk / 2.5f) * 7f;
            float roll = sideS + amp * Mathf.Sin(swayT * 1.1f), pit = fwdS + amp * 0.5f * Mathf.Sin(swayT * 0.7f + 1f);
            if (Mathf.Abs(roll) > 0.05f || Mathf.Abs(pit) > 0.05f)
            {
                foreach (string sp in new[] { "spine_middle", "spine_upper" })
                {
                    Transform b = Bone(sp);
                    if (b != null) b.rotation = Quaternion.AngleAxis(-roll * 0.5f, r.forward) * Quaternion.AngleAxis(pit * 0.5f, r.right) * b.rotation;
                }
                Transform hp = Bone("HeadPivot") ?? headBone;
                if (hp != null) hp.rotation = Quaternion.AngleAxis(roll * 0.4f, r.forward) * hp.rotation;
            }
            if (chair)
            {
                float dy = Mathf.Clamp(Mathf.DeltaAngle(r.eulerAngles.y, st.Yaw), -80f, 80f);
                Turn(Bone("spine_upper"), dy * 0.25f, 0f);
                Turn(Bone("HeadPivot") ?? headBone, dy * 0.6f, 0f);
            }
        }

        // Gestes en cours par bras (journal, et bras qui ne retombent pas le long du corps).
        void ChooseGestures(int f, Gestures.Remote g)
        {
            int gb = g != null ? g.Bits : 0;
            float now = Time.realtimeSinceStartup;
            bool carry = (f & PlayerSync.F_Carry) != 0;
            float heldSize = g != null && (gb & Gestures.G_Held) != 0 ? g.HeldSize : 0.4f;
            gestR = g != null && now - g.PunchAt < 0.55f ? "coup" : g != null && now - g.FingerAt < 1.6f ? "doigt"
                  : (gb & Gestures.G_Thumb) != 0 ? "pouce" : (gb & Gestures.G_Push) != 0 ? "pousse" : (gb & Gestures.G_Piss) != 0 ? "pipi"
                  : carry ? "porte" : "";
            gestL = (gb & Gestures.G_Watch) != 0 ? "montre" : (gb & Gestures.G_Push) != 0 ? "pousse" : (gb & Gestures.G_Piss) != 0 ? "pipi"
                  : carry && heldSize > 0.25f && (f & PlayerSync.F_Smoke) == 0 ? "porte" : "";
        }

        static void Ease(ref float w, bool on, float rate) { w = Mathf.MoveTowards(w, on ? 1f : 0f, rate); }

        // Bras vers leur cible, du moins prioritaire au plus prioritaire (chacun part de la pose laissee par le
        // precedent) ; poids lisses ; le coup suit une courbe (aller 0,12 s, retour 0,4 s).
        void GesturePose(PlayerState st, int f, Gestures.Remote g)
        {
            Transform r = Root.transform;
            Transform sR = Bone("shoulder_right"), sL = Bone("shoulder_left"), pelvis = Bone("pelvis");
            if (sR == null || sL == null) return;
            int gb = g != null ? g.Bits : 0;
            float now = Time.realtimeSinceStartup;
            float tp = g != null ? now - g.PunchAt : 99f;
            float punch = tp < 0.12f ? tp / 0.12f : tp < 0.52f ? 1f - (tp - 0.12f) / 0.4f : 0f;
            bool finger = g != null && now - g.FingerAt < 1.6f;
            bool carry = (f & PlayerSync.F_Carry) != 0 && !inCar;
            bool piss = (gb & Gestures.G_Piss) != 0 && pelvis != null, push = (gb & Gestures.G_Push) != 0;
            float rate = Time.deltaTime * 5f;
            Ease(ref wR[0], finger, rate); Ease(ref wR[1], (gb & Gestures.G_Thumb) != 0, rate); Ease(ref wR[2], push, rate);
            Ease(ref wR[3], piss, rate); Ease(ref wR[4], carry, rate);
            Ease(ref wL[0], (gb & Gestures.G_Watch) != 0, rate); Ease(ref wL[1], push, rate); Ease(ref wL[2], piss, rate);
            Ease(ref wL[3], gestL == "porte", rate);
            // Objet tenu : place recue (repere de sa camera) ou devant lui ; deux mains de part et d'autre s'il est large.
            if (wR[4] > 0.001f || wL[3] > 0.001f)
            {
                Vector3 head = chair ? st.Head : r.position + (st.Head - st.Feet);
                Vector3 local = g != null && (gb & Gestures.G_Held) != 0 ? g.Held : new Vector3(0f, -0.3f, 0.6f);
                float size = g != null && (gb & Gestures.G_Held) != 0 ? g.HeldSize : 0.4f;
                Vector3 at = head + Quaternion.Euler(Mathf.Clamp(st.Pitch, -80f, 80f), st.Yaw, 0f) * local;
                float half = wL[3] > 0.001f ? Mathf.Min(size * 0.5f, 0.25f) : 0f;
                ArmTo(true, at + r.right * half, wR[4]);
                ArmTo(false, at - r.right * half, wL[3]);
            }
            if (pelvis != null)
            {
                Vector3 crotch = pelvis.position + r.forward * 0.2f - r.up * 0.02f;
                ArmTo(true, crotch + r.right * 0.05f, wR[3]);
                ArmTo(false, crotch - r.right * 0.05f, wL[2]);
            }
            ArmTo(true, sR.position + r.forward * 0.55f - r.up * 0.05f, wR[2]);
            ArmTo(false, sL.position + r.forward * 0.55f - r.up * 0.05f, wL[1]);
            // Pouce leve (auto-stop) : bras tendu sur le cote, un peu en avant.
            ArmTo(true, sR.position + r.right * 0.55f + r.forward * 0.15f + r.up * 0.02f, wR[1]);
            // Montre : poignet gauche devant la poitrine, la tete baissee dessus.
            Vector3 chest = (sR.position + sL.position) * 0.5f;
            ArmTo(false, chest + r.forward * 0.32f - r.up * 0.08f + r.right * 0.05f, wL[0]);
            if (wL[0] > 0.001f) Turn(Bone("HeadPivot") ?? headBone, 0f, 25f * wL[0]);
            // Doigt : main levee devant, a hauteur du visage.
            ArmTo(true, sR.position + r.forward * 0.45f + r.up * 0.25f - r.right * 0.05f, wR[0]);
            // Coup de poing : bras tendu droit devant.
            ArmTo(true, sR.position + r.forward * 0.7f - r.right * 0.12f, punch);
        }

        // IK a deux os du bras (epaule -> coude -> poignet) : la main va vers 'target' (dosage w, depuis sa place
        // actuelle), le coude vers le bas, l'arriere et l'exterieur.
        void ArmTo(bool right, Vector3 target, float w)
        {
            if (w <= 0.001f) return;
            string s = right ? "_right" : "_left";
            Transform sh = Bone("shoulder" + s), el = Bone("arm" + s), ha = Bone("hand" + s);
            if (sh == null || el == null || ha == null) return;
            Transform r = Root.transform;
            Vector3 goal = Vector3.Lerp(ha.position, target, Mathf.Clamp01(w));
            float l1 = (el.position - sh.position).magnitude, l2 = (ha.position - el.position).magnitude;
            if (l1 < 1e-3f || l2 < 1e-3f) return;
            Vector3 d = goal - sh.position;
            float dist = Mathf.Clamp(d.magnitude, Mathf.Abs(l1 - l2) + 0.01f, l1 + l2 - 0.001f);
            Vector3 dir = d.normalized;
            Vector3 hint = -r.up * 0.7f - r.forward * 0.3f + r.right * (right ? 0.4f : -0.4f);
            Vector3 bend = Vector3.ProjectOnPlane(hint, dir).normalized;
            float cosA = Mathf.Clamp((l1 * l1 + dist * dist - l2 * l2) / (2f * l1 * dist), -1f, 1f);
            Vector3 elbow = sh.position + dir * (l1 * cosA) + bend * (l1 * Mathf.Sqrt(1f - cosA * cosA));
            sh.rotation = Quaternion.FromToRotation(el.position - sh.position, elbow - sh.position) * sh.rotation;
            el.rotation = Quaternion.FromToRotation(ha.position - el.position, sh.position + dir * dist - el.position) * el.rotation;
        }

        // Jet : copie de celui du joueur local (PLAYER/.../Piss/Fluid/Fluid, particules), faite au premier pipi.
        void Pee(bool on)
        {
            if (on && pee == null && !peeTried)
            {
                peeTried = true;
                GameObject pl = GameObject.Find("PLAYER");
                Transform piss = pl != null ? pl.transform.Find("Pivot/AnimPivot/Camera/FPSCamera/Piss") : null;
                Transform src = piss != null ? piss.Find("Fluid/Fluid") : null;
                if (src != null)
                {
                    peeLocal = Quaternion.Inverse(piss.rotation) * src.rotation;
                    pee = (GameObject)Object.Instantiate(src.gameObject);
                    foreach (PlayMakerFSM pf in pee.GetComponentsInChildren<PlayMakerFSM>(true)) Object.Destroy(pf);
                    foreach (Collider c in pee.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
                    pee.name = "MWCoop-Jet";
                    pee.transform.parent = Root.transform;
                    pee.SetActive(true);
                }
                else Log.Warn("avatar : jet du joueur introuvable (Piss/Fluid/Fluid)");
            }
            if (pee == null) return;
            var pe = pee.GetComponent<ParticleEmitter>();
            if (pe == null) return;
            pe.emit = on;
            if (on && pe.maxEmission < 1f) { pe.minEmission = 150f; pe.maxEmission = 250f; }
        }

        void PlacePee()
        {
            Transform pv = Bone("pelvis");
            if (pee == null || pv == null) return;
            Transform r = Root.transform;
            pee.transform.position = pv.position + r.forward * 0.17f - r.up * 0.03f;
            pee.transform.rotation = r.rotation * Quaternion.Euler(15f, 0f, 0f) * peeLocal;
        }

        // Essais (Gestures.Test) : ce que montre l'avatar.
        public string GestureState()
        {
            Transform r = Root.transform;
            Gestures.Remote g = Gestures.Of(Player.Id);
            var sb = new System.Text.StringBuilder();
            sb.Append("recu ").Append(g != null ? Gestures.Names(g.Bits) : "rien");
            if (g != null) sb.Append(", ivresse ").Append(g.Drunk.ToString("F2")).Append(", penche ").Append(g.LeanSide.ToString("F0")).Append('/').Append(g.LeanFwd.ToString("F0"));
            sb.Append(" | corps ").Append(body0 ?? "-").Append(", bras d '").Append(gestR).Append("' g '").Append(gestL).Append('\'');
            sb.Append(", a terre ").Append(lieW.ToString("F2")).Append(", buste ").Append(sideS.ToString("F0")).Append('/').Append(fwdS.ToString("F0"));
            Transform hr = Bone("hand_right"), hl = Bone("hand_left"), pv = Bone("pelvis");
            if (hr != null && hl != null) sb.Append(", mains ").Append(r.InverseTransformPoint(hr.position).ToString("F2")).Append(' ').Append(r.InverseTransformPoint(hl.position).ToString("F2"));
            if (chair && g != null && pv != null)
            {
                Vector3 e = pv.position - g.Seat; e.y = 0f;
                sb.Append(", sur le siege ").Append(g.Seat.ToString("F2")).Append(" sens ").Append(g.SeatYaw.ToString("F0"))
                  .Append(", bassin ").Append(pv.position.ToString("F2")).Append(" (ecart ").Append(e.magnitude.ToString("F2")).Append(" m)");
            }
            if (pee != null) { var pe = pee.GetComponent<ParticleEmitter>(); sb.Append(", jet ").Append(pe != null && pe.emit).Append(' ').Append(pe != null ? pe.particleCount : 0); }
            sb.Append(", racine ").Append(r.position.ToString("F2")).Append(" yaw ").Append(r.eulerAngles.y.ToString("F0")).Append(" incl ").Append(Mathf.DeltaAngle(0f, r.eulerAngles.x).ToString("F0"));
            return sb.ToString();
        }

        // Geste d'un bras sur l'Animation de son epaule. Sans geste : le balancement de marche ; a
        // l'arret, ce meme clip fige au quart du cycle (bras a la verticale, le long du corps).
        static void ArmPlay(Animation a, string gesture, string walk, bool moving)
        {
            if (a == null) return;
            string clip = gesture ?? walk;
            AnimationState s = a[clip];
            if (s == null) return;
            if (!a.IsPlaying(clip)) a.CrossFade(clip, 0.3f);
            if (gesture != null) return;
            if (moving) { if (s.speed == 0f) s.speed = 1f; }
            else { s.speed = 0f; s.normalizedTime = Mathf.MoveTowards(s.normalizedTime % 1f, 0.25f, Time.deltaTime); }
        }

        public void Apply(PlayerInfo pi)
        {
            PlayerState st = pi.State;
            int f = st.Flags;
            int cloth = f & (PlayerSync.F_Jacket | PlayerSync.F_Coverall);
            if ((skin != pi.Skin || cloth != clothFlags) && body != null)
            {
                skin = pi.Skin;
                clothFlags = cloth;
                ApplyMaterial();
            }
            Gestures.Remote g = Gestures.Of(pi.Id);
            bool down = g != null && (g.Bits & Gestures.G_Down) != 0;
            Vector3 seatPos;
            Quaternion seatRot;
            inCar = VehicleSync.SeatPose(pi.Id, st.Head, out seatPos, out seatRot);
            Transform pCar = null; Vector3 pHead = Vector3.zero; string pName = null;
            passenger = !inCar && Seats.RemoteSeat(pi.Id, out pCar, out pHead, out pName);
            if (passenger)
            {
                // Passager : la place fixe de la voiture locale, assis, la tete suit son regard.
                inCar = true;
                carName = pName;
                Root.transform.rotation = pCar.rotation;
                leanOff = Vector3.zero;
                pos = pCar.TransformPoint(pHead) - pCar.rotation * eyesRest;
                yaw = pCar.eulerAngles.y;
                placed = true;
                Root.transform.position = pos;
                f |= PlayerSync.F_Seated;
            }
            else if (inCar)
            {
                carName = VehicleSync.RemoteCarName(pi.Id);
                // Au volant : oriente comme la voiture locale. Le corps est ancre sur le siege (la tete au
                // repos, apprise quand elle bouge peu) ; quand la camera s'en ecarte (se pencher avec E,
                // tourner la tete), c'est le buste qui se penche, pas tout le corps qui glisse.
                Root.transform.rotation = seatRot;
                Transform carT = VehicleSync.RemoteCarTransform(pi.Id);
                Vector3 headLocal = carT != null ? carT.InverseTransformPoint(seatPos) : Vector3.zero;
                if (!anchorSet || anchorCar != carName) { seatAnchor = headLocal; anchorSet = true; anchorCar = carName; }
                Vector3 off = headLocal - seatAnchor;
                if (off.sqrMagnitude < 0.05f * 0.05f) seatAnchor = Vector3.Lerp(seatAnchor, headLocal, Time.deltaTime * 0.5f);
                leanOff = Vector3.ClampMagnitude(headLocal - seatAnchor, 0.6f);
                Vector3 anchorWorld = carT != null ? carT.TransformPoint(seatAnchor) : seatPos;
                pos = anchorWorld - seatRot * eyesRest;
                yaw = seatRot.eulerAngles.y;
                placed = true;
                Root.transform.position = pos;
                f |= PlayerSync.F_Seated;
            }
            else
            {
                anchorSet = false;
                if (!placed) { pos = st.Feet; yaw = st.Yaw; placed = true; }
                // Lissage : rattrape l'etat recu (20/s) sans a-coups ; teleportation au-dela de 8 m.
                float k = 1f - Mathf.Exp(-12f * Time.deltaTime);
                if ((st.Feet - pos).sqrMagnitude > 64f) pos = st.Feet; else pos = Vector3.Lerp(pos, st.Feet, k);
                yaw = Mathf.LerpAngle(yaw, st.Yaw, k);
                // Essais : avatar de profil face a la camera locale (TestProfil=angle).
                string prof = Config.Get("Test", "TestProfil", "");
                if (prof.Length > 0 && PlayerSync.LocalCamera != null) yaw = PlayerSync.LocalCamera.eulerAngles.y + float.Parse(prof, System.Globalization.CultureInfo.InvariantCulture);
                Root.transform.position = pos;
                Root.transform.rotation = Quaternion.Euler(0, yaw, 0);
            }
            // Dort (lit, chalet, cabine du camion) : couche sur le dos, la tete a la place de la camera du dormeur (envoyee),
            // les pieds dans la direction de son regard, bras le long du corps (la pose « a terre », ancree a la tete). Avant :
            // pose assise au pied du lit (retour de JD, 06/10).
            bool sleep = !inCar && !down && (f & PlayerSync.F_Sleep) != 0;
            if (sleep)
            {
                Vector3 fwd = Quaternion.Euler(0f, st.Yaw, 0f) * Vector3.forward;
                pos = st.Head + fwd * SleepEyes - Vector3.up * 0.3f;   // (pieds ; la pose couchee remonte le corps de 0,14 m : un peu dans la couette)
                yaw = st.Yaw;
                Root.transform.position = pos;
                Root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                if (Time.realtimeSinceStartup >= sleepDiag && headBone != null)
                {
                    sleepDiag = Time.realtimeSinceStartup + 10f;
                    Vector3 hb = headBone.position;
                    RaycastHit wall;
                    bool through = Physics.Linecast(st.Head, pos + Vector3.up * 0.3f, out wall) && !wall.collider.isTrigger;
                    Log.Info("avatar " + Player.Name + " couche : tete (os) " + hb.ToString("F2") + ", camera recue " + st.Head.ToString("F2") + ", pieds " + pos.ToString("F2") + ", cap " + yaw.ToString("F0")
                             + (through ? ", ENTRE TETE ET PIEDS : " + Recon.Path(wall.collider.transform) : ", rien entre tete et pieds"));
                }
            }
            // Assis sur un siege du jeu (chaise, canape, banc, sauna) : pose sur le siege envoye, dans son sens, le
            // bassin (mesure dans la pose assise, LatePose) au-dessus du milieu du siege ; les pieds au sol du meuble.
            chair = !inCar && (f & PlayerSync.F_Seated) != 0 && g != null && (g.Bits & Gestures.G_Seat) != 0 && !down;
            if (chair)
            {
                Quaternion sr = Quaternion.Euler(0f, g.SeatYaw, 0f);
                pos = g.Seat - sr * new Vector3(sitPelvis.x, 0f, sitPelvis.z);
                // La pose assise (worker1_sitdown) suppose une chaise haute : bassin a sitPelvis.y du sol. Sur un canape
                // l'assise est plus basse (l'avatar flottait au-dessus des coussins, pieds dans le vide) : hauteur
                // reelle du coussin sous le siege (rayon vertical, 0,2 a 0,75 m au-dessus du sol), bassin pose dessus.
                if (g.Seat != chairSeatAt)
                {
                    chairSeatAt = g.Seat;
                    float top = g.Seat.y + 0.45f;
                    float best = -1f;
                    foreach (RaycastHit h in Physics.RaycastAll(g.Seat + Vector3.up * 1.5f, Vector3.down, 1.6f))
                    {
                        if (h.collider.isTrigger || h.transform.IsChildOf(Root.transform)) continue;
                        float dy = h.point.y - g.Seat.y;
                        if (dy > 0.2f && dy < 0.75f && dy > best) best = dy;
                    }
                    if (best > 0f) top = g.Seat.y + best;
                    chairTop = top - g.Seat.y;
                }
                // (sitPelvis n'est mesure qu'apres quelques images de pose assise : recalcule a chaque image)
                pos.y -= Mathf.Clamp(sitPelvis.y - chairTop - 0.06f, 0f, 0.6f);
                yaw = g.SeatYaw;
                Root.transform.position = pos;
                Root.transform.rotation = sr;
            }
            else sitMeasured = false;
            // A terre (evanoui, assomme) : le corps tombe en arriere, pivot aux pieds, et reste allonge sur le dos.
            lieW = sleep ? 1f : Mathf.MoveTowards(lieW, down && !inCar ? 1f : 0f, Time.deltaTime * (down ? 1.2f : 2f));
            if (lieW > 0.001f && !inCar)
            {
                Root.transform.rotation = Root.transform.rotation * Quaternion.Euler(-90f * Mathf.SmoothStep(0f, 1f, lieW), 0f, 0f);
                Root.transform.position += Vector3.up * 0.14f * lieW;
            }
            // Assis (vehicule, chaise), couche : pose assise ; accroupi : pose debout pliee (LatePose). A terre : debout raide.
            bool crouch = (f & PlayerSync.F_Crouch) != 0 && (f & (PlayerSync.F_Seated | PlayerSync.F_Sleep)) == 0 && !down;
            bool sit = (f & PlayerSync.F_Seated) != 0 && !down && !sleep;
            crouching = crouch;
            // Profondeur : hauteur de camera de l'automate Crouch du joueur (1,4 debout -> 0,3 au ras du sol).
            // Anciennes versions (hauteur du corps envoyee, > 1,4) : accroupi simple.
            crouchDepth = !crouch ? 0f : st.Height > 1.45f ? 0.5f : Mathf.Clamp01((1.4f - st.Height) / 1.1f);
            sitting = sit || crouch;
            Root.transform.localScale = Vector3.one;
            if (Time.realtimeSinceStartup >= nextDiag && body != null)
            {
                nextDiag = Time.realtimeSinceStartup + 10f;
                Log.Info("avatar " + Player.Name + " : pieds " + pos.ToString("F2") + ", rendu " + body.enabled
                         + "/" + body.gameObject.activeInHierarchy + ", visible " + body.isVisible
                         + ", boite " + body.bounds.min.ToString("F2") + " - " + body.bounds.max.ToString("F2")
                         + ", couche " + body.gameObject.layer + ", anim " + (anim != null && anim.isPlaying));
            }
            if (anim != null && ForceClip != null)
            {
                if (anim[ForceClip] == null) { AnimationClip fc = FindClip(ForceClip); if (fc != null) anim.AddClip(fc, ForceClip); }
                if (anim[ForceClip] != null)
                {
                    if (!anim.IsPlaying(ForceClip)) anim.Play(ForceClip, PlayMode.StopAll);
                    anim[ForceClip].speed = 0f;
                    anim[ForceClip].normalizedTime = ForceTime;
                }
                if (armR != null) armR.Stop();
                if (armL != null) armL.Stop();
                body0 = null;
                return;
            }
            if (anim != null)
            {
                moving = st.Speed > 0.3f && !sit;
                string clip = sit && anim["assis"] != null ? "assis" : moving ? "fat_walk" : "fat_standing";
                if (anim[clip] != null)
                {
                    if (clip == "fat_walk") anim[clip].speed = Mathf.Clamp(st.Speed / 1.4f, 0.6f, 2.5f);
                    if (body0 != clip)
                    {
                        body0 = clip;
                        if (clip == "assis") anim[clip].time = 0;
                        anim.CrossFade(clip, 0.3f, PlayMode.StopSameLayer);
                    }
                }
                bool helloNow = (f & PlayerSync.F_Hello) != 0;
                if (helloNow && !wasHello && armR != null && armR["saluer"] != null) { armR.Stop(); armR.Play("saluer"); }
                wasHello = helloNow;
                if (armR != null && !armR.IsPlaying("saluer"))
                    ArmPlay(armR, (f & PlayerSync.F_Drink) != 0 ? "boire" : (f & PlayerSync.F_Carry) != 0 ? "porter" : null, "marche_d", moving);
                if ((f & PlayerSync.F_Smoke) != 0 && armL != null && armL["fumer"] != null) SmokeArm(f);
                else ArmPlay(armL, null, "marche_g", moving);
                Cigarette((f & PlayerSync.F_Smoke) != 0, (f & PlayerSync.F_Exhale) != 0);
                DrinkInHand((f & PlayerSync.F_Drink) != 0 ? st.Drink : 0);
                Pee(!inCar && !down && g != null && (g.Bits & Gestures.G_Piss) != 0);
            }
        }

        // Cigarette : le bras gauche (clip fumer, fige) monte la main a la bouche tant que le joueur tire
        // (touche tenue), redescend quand il relache. Instants du clip pris une fois : main la plus pres
        // de la tete (bouche) et la plus loin (bras baisse).
        float smokeRaise, tUp = -1f, tDown;
        GameObject cig, smoke;
        bool wasExhale; float exhaleAt;

        void SmokeArm(int f)
        {
            AnimationState s = armL["fumer"];
            if (!armL.IsPlaying("fumer")) armL.Play("fumer");
            s.speed = 0f;
            if (tUp < 0f)
            {
                Transform hand = Bone("hand_left"), head = headBone;
                float best = float.MaxValue, worst = -1f;
                tUp = 0.5f; tDown = 0f;
                if (hand != null && head != null)
                    for (int i = 0; i <= 20; i++)
                    {
                        s.normalizedTime = i / 20f;
                        armL.Sample();
                        float d = (hand.position - head.position).sqrMagnitude;
                        if (d < best) { best = d; tUp = i / 20f; }
                        if (d > worst) { worst = d; tDown = i / 20f; }
                    }
            }
            smokeRaise = Mathf.MoveTowards(smokeRaise, (f & PlayerSync.F_Inhale) != 0 ? 1f : 0f, Time.deltaTime * 2.5f);
            s.normalizedTime = Mathf.Lerp(tDown, tUp, smokeRaise);
        }

        // Cigarette dans la main gauche (copie de celle du joueur local) et fumee a la bouche (copie de
        // son BreathSmoke), creees a la premiere cigarette.
        void Cigarette(bool on, bool exhale)
        {
            if (on && cig == null)
            {
                // Petite cigarette faite ici (cylindre de 8 cm), avec la matiere de celle du joueur local : le
                // modele du jeu est en plusieurs pieces calees pour la vue a la premiere personne.
                cig = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.Destroy(cig.GetComponent<Collider>());
                cig.name = "MWCoop-Cigarette";
                cig.transform.parent = Root.transform;
                cig.transform.localScale = new Vector3(0.009f, 0.04f, 0.009f);
                Shader diff = Shader.Find("Diffuse");
                if (diff != null) cig.GetComponent<Renderer>().material = new Material(diff) { color = new Color(0.95f, 0.93f, 0.88f) };
                // Bout rougeoyant.
                GameObject tip = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.Destroy(tip.GetComponent<Collider>());
                tip.transform.parent = cig.transform;
                tip.transform.localScale = new Vector3(1.05f, 0.12f, 1.05f);
                tip.transform.localPosition = new Vector3(0f, 0.95f, 0f);
                Shader unlit = Shader.Find("Unlit/Color") ?? diff;
                if (unlit != null) tip.GetComponent<Renderer>().material = new Material(unlit) { color = new Color(1f, 0.35f, 0.05f) };
                GameObject pl = GameObject.Find("PLAYER");
                Transform bs = pl != null ? FindBone(pl.transform, "BreathSmoke") : null;
                if (bs != null)
                {
                    smoke = (GameObject)Object.Instantiate(bs.gameObject);
                    foreach (PlayMakerFSM pf in smoke.GetComponentsInChildren<PlayMakerFSM>(true)) Object.Destroy(pf);
                    smoke.name = "MWCoop-Fumee";
                    smoke.transform.parent = Root.transform;
                    smoke.SetActive(true);
                }
            }
            if (cig != null && cig.activeSelf != on) cig.SetActive(on);
            if (smoke != null)
            {
                var pe = smoke.GetComponent<ParticleEmitter>();
                // Une bouffee : 1,2 s au debut de l'expiration, moins dense que chez le joueur (vue de pres).
                if (exhale && !wasExhale) exhaleAt = Time.realtimeSinceStartup;
                wasExhale = exhale;
                bool puff = on && exhale && Time.realtimeSinceStartup - exhaleAt < 1.2f;
                if (pe != null) { pe.emit = puff; if (puff) pe.maxEmission = 4f; }
            }
        }

        // Boisson en main (Drinks) : copie de l'objet que le joueur tient en buvant (biere, lait, soda, cafe...),
        // dans la main droite (clip « boire »), ramenee a une taille de bouteille ; retiree quand il a fini.
        GameObject drinkGo;
        int drinkIdx;
        Vector3 drinkAxis = Vector3.up;   // axe long du modele, du cul vers le goulot (repere de l'objet)
        float drinkHalf = 0.12f;           // demi-longueur une fois a l'echelle (m)
        float drinkLogAt;

        void DrinkInHand(int i)
        {
            if (i == drinkIdx) { if (drinkGo != null && drinkGo.activeSelf != (i > 0)) drinkGo.SetActive(i > 0); return; }
            drinkIdx = i;
            if (drinkGo != null) { Object.Destroy(drinkGo); drinkGo = null; }
            if (i <= 0) return;
            drinkGo = Drinks.Model(i, Root.transform);
            if (drinkGo == null) return;
            // Taille : la plus grande dimension de ses rendus ramenee a 24 cm (bouteilles) ou 11 cm (tasses, verres).
            Bounds b = new Bounds(drinkGo.transform.position, Vector3.zero);
            bool any = false;
            foreach (Renderer r in drinkGo.GetComponentsInChildren<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            string n = Drinks.Names[i];
            float want = n.Contains("Coffee") || n.Contains("Glass") ? 0.11f : 0.24f;
            float size = any ? Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) : 0f;
            if (size > 0.01f) drinkGo.transform.localScale *= want / size;
            drinkHalf = want / 2f;
            DrinkAxis();
            Log.Info("avatar " + Player.Name + " : boit (" + n + ", " + (size > 0f ? (size * 100f).ToString("F0") + " cm ramenes a " + (want * 100f).ToString("F0") : "taille ?") + ")");
        }

        // Axe long du modele (dans son repere : boites des maillages ramenees a l'objet) et sens du goulot : le bout le plus
        // fin (rayon moyen des sommets des 20 % extremes), si les sommets se lisent ; sinon vers +axe. Avant, la bouteille
        // etait tournee comme si son axe etait toujours Y, goulot en haut : couchee ou a l'envers selon le modele, et a la
        // bouche elle partait de travers (retour de JD, 07/10).
        void DrinkAxis()
        {
            drinkAxis = Vector3.up;
            Transform t0 = drinkGo.transform;
            bool any = false;
            Bounds lb = new Bounds();
            var mfs = drinkGo.GetComponentsInChildren<MeshFilter>();
            foreach (MeshFilter mf in mfs)
            {
                if (mf.sharedMesh == null) continue;
                Bounds mb = mf.sharedMesh.bounds;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((k & 1) != 0 ? 1 : -1, (k & 2) != 0 ? 1 : -1, (k & 4) != 0 ? 1 : -1));
                    Vector3 lp = t0.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    if (!any) { lb = new Bounds(lp, Vector3.zero); any = true; } else lb.Encapsulate(lp);
                }
            }
            if (!any) return;
            Vector3 sz = lb.size;
            int ax = sz.x >= sz.y && sz.x >= sz.z ? 0 : sz.y >= sz.z ? 1 : 2;
            Vector3 axis = ax == 0 ? Vector3.right : ax == 1 ? Vector3.up : Vector3.forward;
            float lo = lb.min[ax], hi = lb.max[ax], len = hi - lo;
            float rLo = 0f, rHi = 0f; int nLo = 0, nHi = 0;
            try
            {
                foreach (MeshFilter mf in mfs)
                {
                    if (mf.sharedMesh == null) continue;
                    foreach (Vector3 v in mf.sharedMesh.vertices)
                    {
                        Vector3 lp = t0.InverseTransformPoint(mf.transform.TransformPoint(v));
                        float along = lp[ax];
                        Vector3 rel = lp - lb.center; rel[ax] = 0f;
                        if (along < lo + len * 0.2f) { rLo += rel.magnitude; nLo++; }
                        else if (along > hi - len * 0.2f) { rHi += rel.magnitude; nHi++; }
                    }
                }
            }
            catch (System.Exception) { nLo = nHi = 0; }   // (maillage non lisible : axe seul)
            bool neckHigh = nLo == 0 || nHi == 0 || rHi / nHi <= rLo / nLo;
            drinkAxis = neckHigh ? axis : -axis;
            Log.Info("avatar " + Player.Name + " : boisson, axe " + "XYZ"[ax] + (neckHigh ? "+" : "-") + (nLo > 0 && nHi > 0 ? " (goulot d'apres les sommets)" : ""));
        }

        void PlaceDrink()
        {
            if (drinkGo == null || !drinkGo.activeSelf) return;
            Transform hand = Bone("hand_right"), finger = Bone("finger_right");
            if (hand == null) return;
            // Toujours dans le poing (entre le poignet et le bout des doigts) : debout, la main un peu sous le milieu. Quand la
            // main monte a la bouche (clip "boire"), elle bascule du poing VERS la bouche, un peu goulot en bas (cul en l'air) :
            // la main reste sur l'axe et le goulot arrive aux levres. (Avant, elle glissait vers un point fixe devant la
            // bouche et quittait la main a mi-chemin.)
            Vector3 fist = finger != null ? Vector3.Lerp(hand.position, finger.position, 0.55f) : hand.position;
            Vector3 up = Root.transform.up, fwd = Root.transform.forward;
            Vector3 mouth = headBone != null ? headBone.position + fwd * 0.09f - up * 0.07f : fist;
            float dist = Vector3.Distance(fist, mouth);
            float t = headBone != null ? Mathf.Clamp01(1f - (dist - 0.12f) / 0.20f) : 0f;
            Vector3 toMouth = dist > 0.01f ? (mouth - fist) / dist : -fwd;
            Vector3 dir = Vector3.Slerp(up, (toMouth - up * 0.45f).normalized, t).normalized;   // du cul vers le goulot
            if (Time.realtimeSinceStartup >= drinkLogAt && Config.GetInt("Test", "JournalBoisson", 0) != 0)
            {
                drinkLogAt = Time.realtimeSinceStartup + 1f;
                Log.Info("boisson : poing-bouche " + Vector3.Distance(fist, mouth).ToString("F2") + " m, bascule " + t.ToString("F2") + ", poing " + Root.transform.InverseTransformPoint(fist).ToString("F2")
                         + ", tete " + (headBone != null ? Root.transform.InverseTransformPoint(headBone.position).ToString("F2") : "?") + ", axe " + drinkAxis);
            }
            drinkGo.transform.rotation = Quaternion.FromToRotation(drinkAxis, dir);
            // Centre de la bouteille sur son axe, depuis le poing : un peu au-dessus en la tenant ; en buvant, de sorte que le
            // goulot (centre + demi-longueur) touche la bouche, sans que la main quitte la bouteille.
            float along = Mathf.Lerp(0.2f, Mathf.Clamp(dist / drinkHalf - 0.95f, -0.6f, 0.6f), t);
            Vector3 target = fist + dir * drinkHalf * along;
            // Le centre des rendus est pose la (l'origine du modele du jeu n'est pas forcement en son milieu).
            Vector3 c = drinkGo.transform.position;
            bool any = false; Bounds b = new Bounds();
            foreach (Renderer r in drinkGo.GetComponentsInChildren<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            drinkGo.transform.position = any ? target + (c - b.center) : target;
        }

        // Apres la pose : la cigarette entre les doigts, la fumee devant la bouche.
        void PlaceCigarette()
        {
            Transform hand = Bone("hand_left"), sh = Bone("shoulder_left");
            if (cig != null && cig.activeSelf && hand != null && sh != null)
            {
                // Entre les doigts : un peu au-dela du poignet, en travers de l'avant-bras ; c'est le CENTRE
                // du modele (pas son origine, au bout) qui est pose la.
                // Reperes du modele lui-meme (epaules), pas de la racine de l'avatar.
                Transform shr = Bone("shoulder_right");
                Vector3 right = shr != null ? (shr.position - sh.position).normalized : Root.transform.right;
                // Axe du cylindre en travers de la main, le bout rougeoyant (+y) vers l'EXTERIEUR (main gauche : a l'oppose
                // de l'epaule droite) ; il etait cote paume, filtre dehors (retour de JD du 05/10).
                cig.transform.rotation = Quaternion.FromToRotation(Vector3.up, -right);
                // L'os de la main est au poignet, celui des doigts au bout : la cigarette est tenue au bout des doigts.
                Transform finger = Bone("finger_left");
                Vector3 at = finger != null ? Vector3.Lerp(hand.position, finger.position, 0.8f) : hand.position + (hand.position - sh.position).normalized * 0.08f;
                cig.transform.position = at;
            }
            if (smoke != null && headBone != null) smoke.transform.position = headBone.position + Root.transform.forward * 0.12f - Root.transform.up * 0.05f;
        }

        // ---------------------------------------------------------------- vetements (Wear)
        // Veste, combinaison : l'objet du jeu n'est qu'un rouleau de tissu ; le corps prend la teinte du tissu (moyenne
        // de sa texture : veste brune, combinaison camouflage bleutee), ou une autre matiere de PNJ si [Coop]
        // ApparenceVeste / ApparenceCombinaison en nomme une. Casque : copie de ses maillages, sur la tete.
        int clothFlags;
        Material baseMat, clothMat;
        GameObject helmet;
        float nextHelmetTry;

        void ApplyMaterial()
        {
            Material m = FindMaterial(skin) ?? baseMat ?? body.sharedMaterial;
            baseMat = m;
            if (clothMat != null) { Object.Destroy(clothMat); clothMat = null; }
            int kind = (clothFlags & PlayerSync.F_Coverall) != 0 ? 2 : (clothFlags & PlayerSync.F_Jacket) != 0 ? 1 : 0;
            if (kind != 0 && m != null)
            {
                // Autre matiere seulement si le reglage en nomme une (vide par defaut : teinte).
                string sw = Config.Get("Coop", kind == 1 ? "ApparenceVeste" : "ApparenceCombinaison", "");
                Material swap = sw.Length > 0 ? FindMaterial(sw) : null;
                if (swap != null) m = swap;
                else if (m.HasProperty("_Color"))
                {
                    clothMat = new Material(m);
                    Color tint = ParseColor(Config.Get("Coop", kind == 1 ? "TeinteVeste" : "TeinteCombinaison", kind == 1 ? "1,0.83,0.72" : "0.57,0.86,1"));
                    clothMat.color = m.color * tint;
                    m = clothMat;
                }
            }
            if (m != null) body.sharedMaterial = m;
        }

        static Color ParseColor(string s)
        {
            string[] c = s.Split(',');
            float r, g, b;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            if (c.Length < 3 || !float.TryParse(c[0], System.Globalization.NumberStyles.Float, ci, out r)
                || !float.TryParse(c[1], System.Globalization.NumberStyles.Float, ci, out g) || !float.TryParse(c[2], System.Globalization.NumberStyles.Float, ci, out b)) return Color.white;
            return new Color(r, g, b, 1f);
        }

        // Avant la pose (LatePose) : la tete est encore dans la pose du clip, a peu pres droite ; le casque y est pose
        // une fois (droit, devant comme l'avatar), puis suit la tete.
        void Helmet(bool on)
        {
            if (on && helmet == null && Time.realtimeSinceStartup >= nextHelmetTry) BuildHelmet();
            if (helmet != null && helmet.activeSelf != on) helmet.SetActive(on);
        }

        void BuildHelmet()
        {
            nextHelmetTry = Time.realtimeSinceStartup + 10f;
            GameObject src = Wear.ItemObject(2);
            Transform hp = Bone("HeadPivot") ?? headBone;
            if (src == null || hp == null || headBone == null) return;
            Transform st = src.transform;
            helmet = new GameObject("MWCoop-Casque");
            Transform ht = helmet.transform;
            // Maillages seulement (pas l'objet du jeu : ses automates et sa physique), poses comme dans l'objet.
            foreach (MeshFilter mf in src.GetComponentsInChildren<MeshFilter>(true))
            {
                MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || mf.sharedMesh == null) continue;
                var g = new GameObject(mf.name);
                g.transform.parent = ht;
                g.transform.localPosition = st.InverseTransformPoint(mf.transform.position);
                g.transform.localRotation = Quaternion.Inverse(st.rotation) * mf.transform.rotation;
                Vector3 ls = mf.transform.lossyScale, ss = st.lossyScale;
                g.transform.localScale = new Vector3(ls.x / ss.x, ls.y / ss.y, ls.z / ss.z);
                g.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                g.AddComponent<MeshRenderer>().sharedMaterials = mr.sharedMaterials;
            }
            // [Coop] CasquePose = haut, avant (m, depuis l'os de la tete), rotation x, y, z (degres) : modele droit,
            // visiere vers +Z, centre vers la tete.
            string[] p = Config.Get("Coop", "CasquePose", "0.09,0.02,0,0,0").Split(',');
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float[] v = new float[5];
            for (int i = 0; i < 5 && i < p.Length; i++) float.TryParse(p[i], System.Globalization.NumberStyles.Float, ci, out v[i]);
            Transform r = Root.transform;
            ht.localScale = st.lossyScale;
            ht.position = headBone.position + r.up * v[0] + r.forward * v[1];
            ht.rotation = r.rotation * Quaternion.Euler(v[2], v[3], v[4]);
            ht.SetParent(hp, true);
            Log.Info("avatar " + Player.Name + " : casque pose sur " + hp.name + " (" + ht.childCount + " maillages)");
        }

        // Essais (Wear.State) : ce que l'avatar montre.
        public string ClothesState()
        {
            return Player.Name + " : " + ((clothFlags & PlayerSync.F_Coverall) != 0 ? "combinaison" : (clothFlags & PlayerSync.F_Jacket) != 0 ? "veste" : "sans veste")
                   + (clothMat != null ? " (teinte " + clothMat.color + ")" : "")
                   + (helmet != null && helmet.activeSelf ? ", casque" : ", sans casque");
        }

        public void Destroy()
        {
            if (Root != null) Object.Destroy(Root);
            Root = null;
            if (clothMat != null) Object.Destroy(clothMat);
            clothMat = null;
        }
    }
}
