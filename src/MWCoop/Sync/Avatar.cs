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
        Vector3 seatAnchor;                  // tete au repos sur le siege (repere voiture)
        bool anchorSet;
        Vector3 leanOff;                     // ecart camera - ancre (repere voiture) : se pencher
        Vector3 eyesRest = new Vector3(0f, 1.2f, 0.1f);   // yeux / avatar, pose de conduite sans penche
        string anchorCar;
        bool passenger;                      // assis a une place passager (pose assise, pas de volant)
        string carName;
        float armWR, armWL, crouchW, crouchDepth;
        bool crouching;
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
            a.charT = ch.transform;
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
            Pose();
            PlaceCigarette();
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
            if (facingFrames < 0 || charT == null || sitting || inCar) return;
            if (++facingFrames < 10) return;
            facingFrames = -1;
            Transform sr = Bone("shoulder_right"), sl = Bone("shoulder_left");
            if (sr == null || sl == null) return;
            Vector3 right = Root.transform.InverseTransformDirection(sr.position - sl.position);
            right.y = 0f;
            if (right.sqrMagnitude < 1e-4f) return;
            Vector3 fwd = Vector3.Cross(right.normalized, Vector3.up);
            float ang = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            charT.localRotation = Quaternion.Euler(0f, -ang, 0f) * charT.localRotation;
            headRestSet = false;   // la tete au repos se reprend dans le bon sens
            Log.Info("avatar " + Player.Name + " : modele tourne de " + (-ang).ToString("F0") + " deg pour regarder devant");
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
            if ((f & PlayerSync.F_Sleep) != 0 || ForceClip != null) return;
            // Accroupi (deux niveaux, comme le jeu) : voir Crouch.
            crouchW = Mathf.MoveTowards(crouchW, crouchDepth, Time.deltaTime * 3f);
            if (crouchW > 0.001f) Crouch(crouchW);
            // Buste et tete suivent le regard : penche en avant en regardant en bas, en arriere en haut.
            Turn(Bone("spine_middle"), 0f, pitch * 0.15f);
            Turn(Bone("spine_upper"), 0f, pitch * 0.2f);
            Turn(Bone("HeadPivot") ?? headBone, 0f, pitch * 0.5f);
            // Au repos et sans geste : bras le long du corps.
            bool idle = !moving && (!sitting || crouching);   // accroupi : bras le long du corps / vers le sol
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
            // Assis (vehicule, chaise), couche : pose assise ; accroupi : pose debout pliee (LatePose).
            bool crouch = (f & PlayerSync.F_Crouch) != 0 && (f & (PlayerSync.F_Seated | PlayerSync.F_Sleep)) == 0;
            bool sit = (f & (PlayerSync.F_Seated | PlayerSync.F_Sleep)) != 0;
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
                cig.transform.rotation = Quaternion.FromToRotation(Vector3.up, right);   // axe du cylindre : en travers de la main
                // L'os de la main est au poignet, celui des doigts au bout : la cigarette est tenue au bout des doigts.
                Transform finger = Bone("finger_left");
                Vector3 at = finger != null ? Vector3.Lerp(hand.position, finger.position, 0.8f) : hand.position + (hand.position - sh.position).normalized * 0.08f;
                cig.transform.position = at;
            }
            if (smoke != null && headBone != null) smoke.transform.position = headBone.position + Root.transform.forward * 0.12f - Root.transform.up * 0.05f;
        }

        public void Destroy()
        {
            if (Root != null) Object.Destroy(Root);
            Root = null;
        }
    }
}
