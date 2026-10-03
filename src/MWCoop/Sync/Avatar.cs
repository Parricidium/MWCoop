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
        string carName;
        float armWR, armWL, crouchW;
        bool crouching;
        // Os que l'animation en cours ne pilote pas : nos retouches s'y ajouteraient d'une image a
        // l'autre. On garde la valeur de base et celle posee ; si l'os n'a pas bouge depuis, on le remet.
        Quaternion headRest;                 // tete par rapport a l'avatar, debout (pour viser en voiture)
        bool headRestSet;
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

        public static Material FindMaterial(string name)
        {
            if (materials == null)
            {
                materials = new Dictionary<string, Material>();
                foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(Material)))
                    if (!materials.ContainsKey(o.name)) materials[o.name] = (Material)o;
            }
            Material m;
            return materials.TryGetValue(name ?? "", out m) ? m : null;
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
            a.Root = new GameObject("MWCoop-Joueur-" + pi.Id);
            GameObject ch = (GameObject)Object.Instantiate(template);
            ch.name = "Char";
            ch.transform.parent = a.Root.transform;
            ch.transform.localPosition = charOffset;
            ch.transform.localRotation = charRotation;
            ch.transform.localScale = charScale;
            ch.SetActive(true);
            a.anim = ch.GetComponentInChildren<Animation>();
            if (a.anim != null)
            {
                a.anim.cullingType = AnimationCullingType.AlwaysAnimate;
                a.skelRot = a.anim.transform.localRotation;
                a.skelPos = a.anim.transform.localPosition;
                a.headBone = FindBone(a.anim.transform, "head");
                a.bones = new Dictionary<string, Transform>();
                foreach (Transform b in a.anim.GetComponentsInChildren<Transform>(true)) if (!a.bones.ContainsKey(b.name)) a.bones[b.name] = b;
                a.AddClips();
            }
            a.body = ch.GetComponentInChildren<SkinnedMeshRenderer>();
            if (a.body != null) { a.body.updateWhenOffscreen = true; a.body.enabled = true; }
            Log.Info("avatar cree pour " + pi.Name + " (#" + pi.Id + ")");
            return a;
        }

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

        void Crouch(float w)
        {
            Transform pelvis = Bone("pelvis");
            if (pelvis == null) return;
            Transform r = Root.transform;
            Vector3 footL = Bone("ankle_left") != null ? Bone("ankle_left").position : Vector3.zero;
            Vector3 footR = Bone("ankle_right") != null ? Bone("ankle_right").position : Vector3.zero;
            pelvis.position -= r.up * 0.42f * w;
            pelvis.position -= r.forward * 0.08f * w;
            Leg("thig_left", "knee_left", "ankle_left", footL);
            Leg("thig_right", "knee_right", "ankle_right", footR);
            Turn(Bone("spine_middle"), 0f, 18f * w);
            Turn(Bone("spine_upper"), 0f, 10f * w);
            Turn(Bone("HeadPivot") ?? headBone, 0f, -22f * w);   // le regard reste devant
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
            Pose();
            if (Config.GetInt("Test", "JournalPose", 0) != 0 && Time.realtimeSinceStartup >= nextPoseLog && headBone != null && Bone("pelvis") != null)
            {
                nextPoseLog = Time.realtimeSinceStartup + 5f;
                Transform r = Root.transform;
                Vector3 h = r.InverseTransformPoint(headBone.position) - r.InverseTransformPoint(Bone("pelvis").position);
                Vector3 fwd = r.InverseTransformDirection(headBone.forward);
                Log.Info("pose " + Player.Name + " : regard " + Player.State.Pitch.ToString("F0") + ", tete/bassin " + h.ToString("F2") + ", axe tete " + fwd.ToString("F2"));
            }
            for (int i = 0; i < boneList.Length; i++)
            {
                Transform b = boneList[i];
                if (b == null) continue;
                setRot[i] = b.localRotation;
                setPos[i] = b.localPosition;
            }
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
                Dictionary<string, Quaternion> pose = DriverPose(carName);
                if (pose != null)
                    foreach (KeyValuePair<string, Quaternion> kv in pose) { Transform b = Bone(kv.Key); if (b != null) b.localRotation = kv.Value; }
                // Tete : regard relatif a la voiture, sans limite (tour complet accepte), quelle que soit
                // l'inclinaison du dossier.
                Transform hp = Bone("HeadPivot") ?? headBone;
                if (hp != null && headRestSet)
                    hp.rotation = Root.transform.rotation * Quaternion.Euler(pitch, Mathf.DeltaAngle(Root.transform.eulerAngles.y, st.Yaw), 0f) * headRest;
                return;
            }
            if (ForceClip != null && Config.GetInt("Test", "RacineRepos", 1) != 0)
            {
                // Clips d'autres PNJ : ils tournent l'os racine (skeleton) selon leur propre modele.
                anim.transform.localRotation = skelRot;
                anim.transform.localPosition = skelPos;
            }
            if ((f & PlayerSync.F_Sleep) != 0 || ForceClip != null) return;
            // Accroupi : bassin abaisse, jambes pliees par IK (pieds restes au sol), buste penche.
            crouchW = Mathf.MoveTowards(crouchW, crouching ? 1f : 0f, Time.deltaTime * 4f);
            if (crouchW > 0.001f) Crouch(crouchW);
            // Buste et tete suivent le regard : penche en avant en regardant en bas, en arriere en haut.
            Turn(Bone("spine_middle"), 0f, pitch * 0.15f);
            Turn(Bone("spine_upper"), 0f, pitch * 0.2f);
            Turn(Bone("HeadPivot") ?? headBone, 0f, pitch * 0.5f);
            // Au repos et sans geste : bras le long du corps.
            bool idle = !moving && !sitting;
            float dt = Time.deltaTime * 3f;
            armWR = Mathf.MoveTowards(armWR, idle && (f & (PlayerSync.F_Drink | PlayerSync.F_Carry | PlayerSync.F_Hello)) == 0 && (armR == null || !armR.IsPlaying("saluer")) ? 1f : 0f, dt);
            armWL = Mathf.MoveTowards(armWL, idle && (f & PlayerSync.F_Smoke) == 0 ? 1f : 0f, dt);
            ArmDown("shoulder_right", "hand_right", 1f, armWR);
            ArmDown("shoulder_left", "hand_left", -1f, armWL);
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
            if (skin != pi.Skin && body != null)
            {
                skin = pi.Skin;
                Material m = FindMaterial(skin);
                if (m != null) body.sharedMaterial = m;
            }
            PlayerState st = pi.State;
            int f = st.Flags;
            Vector3 seatPos;
            Quaternion seatRot;
            inCar = VehicleSync.SeatPose(pi.Id, st.Head, out seatPos, out seatRot);
            if (inCar)
            {
                carName = VehicleSync.RemoteCarName(pi.Id);
                // Au volant : oriente comme la voiture locale, la tete (yeux) calee sur la camera du
                // joueur, mesuree par rapport a la voiture : il reste assis sur son siege.
                Root.transform.rotation = seatRot;
                Vector3 eyes = headBone != null
                    ? Quaternion.Inverse(Root.transform.rotation) * (headBone.position - Root.transform.position) + EyeOffset
                    : new Vector3(0f, 1.2f, 0.1f);
                pos = seatPos - seatRot * eyes;
                yaw = seatRot.eulerAngles.y;
                placed = true;
                Root.transform.position = pos;
                f |= PlayerSync.F_Seated;
            }
            else
            {
                if (!placed) { pos = st.Feet; yaw = st.Yaw; placed = true; }
                // Lissage : rattrape l'etat recu (20/s) sans a-coups ; teleportation au-dela de 8 m.
                float k = 1f - Mathf.Exp(-12f * Time.deltaTime);
                if ((st.Feet - pos).sqrMagnitude > 64f) pos = st.Feet; else pos = Vector3.Lerp(pos, st.Feet, k);
                yaw = Mathf.LerpAngle(yaw, st.Yaw, k);
                Root.transform.position = pos;
                Root.transform.rotation = Quaternion.Euler(0, yaw, 0);
            }
            // Assis (vehicule, chaise), couche : pose assise ; accroupi : pose debout pliee (LatePose).
            bool crouch = (f & PlayerSync.F_Crouch) != 0 && (f & (PlayerSync.F_Seated | PlayerSync.F_Sleep)) == 0;
            bool sit = (f & (PlayerSync.F_Seated | PlayerSync.F_Sleep)) != 0;
            crouching = crouch;
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
                ArmPlay(armL, (f & PlayerSync.F_Smoke) != 0 ? "fumer" : null, "marche_g", moving);
            }
        }

        public void Destroy()
        {
            if (Root != null) Object.Destroy(Root);
            Root = null;
        }
    }
}
