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
        static GameObject template;          // copie inactive, sans logique (corps du premier marcheur)
        static string defaultBody = "";      // sa cle de corps (Looks.BodyKey)
        static Dictionary<string, GameObject> bodyTemplates = new Dictionary<string, GameObject>();   // autres corps (Looks)
        static Dictionary<string, float> bodyHead = new Dictionary<string, float>();                 // ... tete plus haute (+) ou plus basse (-) que l'os de base (m)
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
            foreach (GameObject t in bodyTemplates.Values) if (t != null) Object.Destroy(t);
            bodyTemplates.Clear();
            bodyHead.Clear();
            Looks.Reset();
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
                StripExtras(template);
                SkinnedMeshRenderer[] bms = template.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                SkinnedMeshRenderer bm = bms.Length > 0 ? bms[0] : null;
                defaultBody = bm != null ? Looks.BodyKey(bm.sharedMesh) : "";
                Log.Info("modele d'avatar : " + Recon.Path(ch) + ", decalage " + charOffset + ", echelle " + charScale);
                return true;
            }
            return false;
        }

        // Modele d'un autre corps (Looks) : le squelette DE BASE (le notre : ses animations seulement ; retour de JD, 08/10 :
        // copie du PNJ, la grand-mere restait assise en l'air, le corps fin marchait sur place avec les clips de son PNJ),
        // avec le maillage de ce corps (os remis dans l'ordre de SON maillage, par leur nom) et ses longueurs d'os (prises
        // de ses poses de liaison, ramenees au repere des os de base) ; jambes plus courtes : modele descendu d'autant.
        static GameObject TemplateFor(string key)
        {
            if (!BuildTemplate()) return null;
            if (string.IsNullOrEmpty(key) || key == defaultBody) return template;
            GameObject t;
            if (bodyTemplates.TryGetValue(key, out t) && t != null) return t;
            Looks.BodyInfo b = Looks.Body(key);
            if (b == null || b.Smr == null) return template;
            t = (GameObject)Object.Instantiate(template);
            t.name = "MWCoop-AvatarModele-" + key;
            t.SetActive(false);
            SkinnedMeshRenderer smr = null;
            foreach (SkinnedMeshRenderer s in t.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (s.bones != null && s.bones.Length >= 10) { smr = s; break; }
            string why = smr == null ? "corps de base introuvable" : null;
            if (smr != null)
            {
                // un PNJ de ce corps dont tous les os existent chez nous (essai sur une copie du rendu de base a chaque fois)
                why = "aucun PNJ de ce corps compatible";
                var tried = new List<SkinnedMeshRenderer>(b.All);
                tried.Sort((x, y) => string.CompareOrdinal(Recon.Path(x.transform), Recon.Path(y.transform)));   // (meme choix chez tous)
                foreach (SkinnedMeshRenderer cand in tried)
                {
                    if (cand == null) continue;
                    string w = Compatible(smr, cand);
                    if (w != null) { why = w; continue; }
                    why = Reshape(smr, cand, key);
                    if (why == null) break;
                }
            }
            if (why != null)
            {
                Object.Destroy(t);
                Log.Warn("modele d'avatar " + key + " : " + why + " ; corps de base garde");
                bodyTemplates[key] = template;
                return template;
            }
            bodyTemplates[key] = t;
            return t;
        }

        // Tous les os de 'src' existent-ils dans le squelette de 'dst' ? null si oui.
        static string Compatible(SkinnedMeshRenderer dst, SkinnedMeshRenderer src)
        {
            if (src.sharedMesh == null || src.bones == null || src.sharedMesh.bindposes.Length != src.bones.Length) return "poses de liaison incompletes";
            var names = new HashSet<string>();
            foreach (Transform x in dst.bones) if (x != null) names.Add(x.name);
            foreach (Transform bb in src.bones) if (bb == null || !names.Contains(bb.name)) return "os " + (bb != null ? bb.name : "?") + " absent du squelette de base";
            return null;
        }

        // Pose 'src' (maillage d'un PNJ) sur le squelette de 'dst' ; null si c'est fait, sinon pourquoi pas.
        // Les os ne bougent pas (en les deplacant, le cou s'etirait) : une copie du maillage recoit les poses de liaison
        // des os DE BASE (meme nom) ; au repos il garde exactement sa forme (petit, fin, rond...), les animations de base
        // le font bouger. Sa tete n'est pas a la hauteur de l'os "head" de base : l'ecart (bodyHead) decale chapeaux et
        // lunettes, et l'apercu du lanceur.
        static string Reshape(SkinnedMeshRenderer dst, SkinnedMeshRenderer src, string key)
        {
            Mesh mb = dst.sharedMesh, mv = src.sharedMesh;
            if (mb == null || mv == null) return "maillage absent";
            Matrix4x4[] bpB = mb.bindposes, bpV = mv.bindposes;
            Transform[] bonesB = dst.bones, bonesV = src.bones;
            var idxB = new Dictionary<string, int>();
            for (int i = 0; i < bonesB.Length; i++) if (bonesB[i] != null) idxB[bonesB[i].name] = i;
            var nb = new Transform[bonesV.Length];
            var nbp = new Matrix4x4[bonesV.Length];
            for (int i = 0; i < bonesV.Length; i++)
            {
                int j;
                if (bonesV[i] == null || !idxB.TryGetValue(bonesV[i].name, out j)) return "os " + (bonesV[i] != null ? bonesV[i].name : "?") + " absent du squelette de base";
                nb[i] = bonesB[j];
                nbp[i] = bpB[j];
            }
            Mesh m;
            try { m = (Mesh)Object.Instantiate(mv); m.name = mv.name; m.bindposes = nbp; }
            catch (System.Exception e) { return "maillage illisible (" + e.Message + ")"; }
            // ecart de hauteur de la tete (poses de liaison inverses : positions dans le repere du maillage), le long de
            // l'axe bassin -> tete du maillage de base (son "haut", quels que soient les reperes) ; metres
            float head = 0f;
            int hv = -1, hb, pb;
            for (int i = 0; i < bonesV.Length; i++) if (bonesV[i] != null && bonesV[i].name == "head") hv = i;
            if (hv >= 0 && idxB.TryGetValue("head", out hb) && idxB.TryGetValue("pelvis", out pb))
            {
                Vector3 hB = bpB[hb].inverse.GetColumn(3), pB = bpB[pb].inverse.GetColumn(3), hV = bpV[hv].inverse.GetColumn(3);
                Vector3 up = (hB - pB).normalized;
                head = Vector3.Dot(hV - hB, up);
            }
            dst.sharedMesh = m;
            dst.bones = nb;
            dst.sharedMaterials = src.sharedMaterials;
            bodyHead[key] = head;
            Log.Info("modele d'avatar " + key + " : maillage de " + Recon.Path(src.transform) + " sur le squelette de base, tete " + (head >= 0 ? "+" : "") + (head * 100f).ToString("F0") + " cm");
            return null;
        }

        // Rien que le corps : les accessoires du PNJ copie (chapeau, lunettes, cigarette...) sont retires.
        static void StripExtras(GameObject go)
        {
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                var s = r as SkinnedMeshRenderer;
                if (s != null && s.bones != null && s.bones.Length >= 10) continue;
                if (r.transform.childCount == 0) Object.DestroyImmediate(r.gameObject);
                else { MeshFilter mf = r.GetComponent<MeshFilter>(); Object.DestroyImmediate(r); if (mf != null) Object.DestroyImmediate(mf); }
            }
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
            if (materials.TryGetValue(name, out m)) return m;
            return CustomFace.IsCustom(name) ? CustomFace.Make(name) : Tenues.Make(name);   // (image importee ; tenue offerte : creee a la demande)
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
            var all = new List<string>(set.Keys);
            all.AddRange(Tenues.List(Tenues.Shirt));   // (tenues offertes, apres celles du jeu)
            return all;
        }

        public static Avatar Create(PlayerInfo pi)
        {
            if (!BuildTemplate()) return null;
            var a = new Avatar { Player = pi };
            a.Build("MWCoop-Joueur-" + pi.Id, Looks.Parse(pi.Skin).Body);
            Log.Info("avatar cree pour " + pi.Name + " (#" + pi.Id + ")");
            return a;
        }

        public string Body = "";             // corps demande (Looks : vide = celui du modele de base)
        Material[] defMats;                  // matieres du modele (haut, pantalon, visage) : champ vide de l'apparence
        public SkinnedMeshRenderer BodyRenderer { get { return body; } }
        // Ecart (monde) entre la tete de ce corps et l'os "head" de base (corps plus petits : plus bas).
        public Vector3 HeadDelta { get { float h; return Root != null && bodyHead.TryGetValue(Body, out h) ? Root.transform.up * h : Vector3.zero; } }

        // Copie du modele (celui du corps 'bodyKey') sous une nouvelle racine 'name', animations pretes.
        void Build(string name, string bodyKey)
        {
            Body = bodyKey ?? "";
            Root = new GameObject(name);
            GameObject ch = (GameObject)Object.Instantiate(TemplateFor(Body) ?? template);
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
            if (body != null) { body.updateWhenOffscreen = true; body.enabled = true; defMats = body.sharedMaterials; }
        }

        // ---------------------------------------------------------------- apercu des tenues (Studio)
        // Personnage seul, sans joueur ni reseau : animations arretees, la pose debout au repos n'est posee que par
        // PoseStanding (echantillons des clips, puis bras le long du corps comme Pose), tourne pour regarder l'avant
        // de sa racine (comme FixFacing), tout sur la couche 'layer'.
        Transform[] restBones;
        Quaternion[] restRot;
        Vector3[] restPos;

        public static Avatar CreatePreview(string name, int layer, string bodyKey = null)
        {
            if (!BuildTemplate()) return null;
            var a = new Avatar();
            a.Build(name, bodyKey);
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
            if (FindMaterial(Looks.Parse(s).Shirt) == null) baseMat = previewDefault;
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
        public Transform HandRight { get { return Bone("hand_right"); } }
        public Transform PelvisBone { get { return Bone("pelvis"); } }
        public Transform VehicleT { get { return inCar ? vehicleT : null; } }

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

        // Retour d'un joueur (10/10, apres 0.66 : « vus d'un passager, le conducteur et les autres glissent en arriere de
        // leur siege et tremblent ») : les avatars sont places a l'etape « joueurs » (Update), la voiture ou le passager
        // local est assis est deplacee ensuite, a l'etape « voitures » (FollowFrame) -- les avatars assis avaient une
        // image de retard sur elle (vitesse x duree d'image : ~30 cm a 70 km/h, variable d'une image a l'autre).
        // Leur place dans la voiture (Apply) est donc reposee ici, sur la voiture a sa pose de cette image.
        bool riding;
        static int noLateAnchor = -1;
        Vector3 rideLocalPos;
        Quaternion rideLocalRot = Quaternion.identity;

        public void LatePose()
        {
            if (anim == null || bones == null || Root == null) return;
            if (noLateAnchor < 0) noLateAnchor = Config.GetInt("Test", "SansAncrageFin", 0);   // (essais : comme avant)
            if (riding && vehicleT != null && noLateAnchor == 0)
            {
                Root.transform.position = vehicleT.TransformPoint(rideLocalPos);
                Root.transform.rotation = vehicleT.rotation * rideLocalRot;
                pos = Root.transform.position;
            }
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
                Dictionary<string, Quaternion> pose = DriverPose(carName);   // (passager : la pose du vehicule aussi -- camion assis droit dans la GIFU ; avant, jambes allongees de voiture, a moitie couche)
                if (pose != null)
                    foreach (KeyValuePair<string, Quaternion> kv in pose)
                    {
                        if (passenger && (kv.Key.Contains("collar") || kv.Key.Contains("shoulder") || kv.Key.Contains("arm") || kv.Key.Contains("hand") || kv.Key.Contains("finger"))) continue;
                        Transform b = Bone(kv.Key); if (b != null) b.localRotation = kv.Value;
                    }
                motoRaise = Vector3.zero;
                seatFitted = false;
                bool moto = carName != null && carName.StartsWith("JONNEZ");
                if (moto) MotoPose(passenger);
                else if (passenger)
                {
                    // Bras poses sur les cuisses.
                    ArmDown("shoulder_right", "hand_right", 1f, 1f);
                    ArmDown("shoulder_left", "hand_left", -1f, 1f);
                    // Se pencher (demande de JD, 10/10) : la touche de penche du jeu (camera tournee), mesuree chez lui
                    // (Gestures) ; buste et tete comme a pied.
                    Gestures.Remote pg = Gestures.Of(Player.Id);
                    sideS = Mathf.MoveTowards(sideS, pg != null ? Mathf.Clamp(pg.LeanSide, -30f, 30f) : 0f, Time.deltaTime * 90f);
                    fwdS = Mathf.MoveTowards(fwdS, pg != null ? Mathf.Clamp(pg.LeanFwd, -30f, 40f) : 0f, Time.deltaTime * 90f);
                    if (Mathf.Abs(sideS) > 0.05f || Mathf.Abs(fwdS) > 0.05f)
                        foreach (string sp in new[] { "spine_middle", "spine_upper" })
                        {
                            Transform b = Bone(sp);
                            if (b != null) b.rotation = Quaternion.AngleAxis(-sideS * 0.5f, Root.transform.forward) * Quaternion.AngleAxis(fwdS * 0.5f, Root.transform.right) * b.rotation;
                        }
                }
                if (!moto) SeatFit(passenger);
                // Yeux au repos (pose de conduite, sans penche) : servent a placer le corps sur le siege.
                if (headBone != null) eyesRest = Quaternion.Inverse(Root.transform.rotation) * (headBone.position - motoRaise - Root.transform.position) + EyeOffset;   // (sans la remontee : sinon l'ancrage la defait)
                // Dos arrondi vers le volant, tete vers le retroviseur (dessin de JD) : apres les yeux au repos (la courbure ne
                // deplace pas le corps a l'image suivante). [Test] SiegeDos / SiegeDos2 en degres (negatif : vers le dossier) ;
                // SiegeTete : tete redressee d'autant (regarde la route, pas le volant).
                if (seatFitted)
                {
                    Turn(Bone("spine_middle"), 0f, Config.GetFloat("Test", "SiegeDos", 18f));
                    Turn(Bone("spine_upper"), 0f, Config.GetFloat("Test", "SiegeDos2", 24f));
                    if (headBone != null) Turn(headBone, 0f, -Config.GetFloat("Test", "SiegeTete", 25f));
                    if (passenger)
                    {   // mains posees sur les cuisses (a mi-chemin du genou), coudes plies
                        foreach (bool right in new[] { true, false })
                        {
                            Transform th = Bone(right ? "thig_right" : "thig_left"), kn = Bone(right ? "knee_right" : "knee_left");
                            if (th != null && kn != null) ArmTo(right, Vector3.Lerp(th.position, kn.position, 0.6f) + vehicleT.up * 0.08f, 1f);
                        }
                    }
                }
                // Se pencher : le buste va vers la camera (cote : autour de l'avant, avant : autour de la droite). Avant les
                // mains : elles se posent ensuite sur le volant depuis le buste penche.
                float side = Mathf.Atan2(leanOff.x, 0.55f) * Mathf.Rad2Deg, fwdLean = Mathf.Atan2(leanOff.z, 0.55f) * Mathf.Rad2Deg;
                foreach (string sp in new[] { "spine_middle", "spine_upper" })
                {
                    Transform b = Bone(sp);
                    if (b == null) continue;
                    b.rotation = Quaternion.AngleAxis(-side * 0.5f, Root.transform.forward) * Quaternion.AngleAxis(fwdLean * 0.5f, Root.transform.right) * b.rotation;
                }
                if (!passenger && !moto) DriverHands();
                if (!moto) SeatLog();
                // Fumer, boire en voiture (demande de JD, 10/10) : comme a pied, la main va a la bouche -- gauche (cigarette)
                // quand il tire, droite (boisson) tant qu'il boit -- depuis le volant ; un passage de vitesse prime sur la
                // boisson (la main droite va au levier, wShift). Avant : les mains restaient au volant.
                Ease(ref wDrink, (f & PlayerSync.F_Drink) != 0 && st.Drink > 0, Time.deltaTime * 3f);
                if (headBone != null)
                {
                    Vector3 mouth = Mouth();
                    Transform rr = Root.transform;
                    ArmTo(true, mouth - DrinkDir() * drinkHalf * 1.1f - rr.up * 0.03f + rr.right * 0.02f, wDrink * (1f - wShift));
                    if ((f & PlayerSync.F_Smoke) != 0) ArmTo(false, mouth - rr.up * 0.03f - rr.right * 0.05f, smokeRaise);
                }
                ReachPose();
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
        float wDrink;

        // ---------------------------------------------------------------- mains du conducteur, gestes vers les commandes
        // Demande d'un joueur (07/10) : mains sur le volant (qui tourne : CarVisuals, lisse), main droite au levier quand
        // il bouge, main vers le bouton / la clef / la molette quand ce joueur s'en sert (Jobs : commande rejouee).
        Transform steerT, gearT, steerCar;
        Vector3 gripR, gripL;              // prises sur la jante, dans le repere du volant (a 10 h 10)
        Quaternion gearLast;
        float gearUntil, wShift, reachUntil, wReach;
        Vector3 reachAt;

        void DriverHands()
        {
            Transform car = VehicleSync.RemoteCarTransform(Player.Id);
            if (car == null || headBone == null) return;
            if (car != steerCar)
            {
                steerCar = car; steerT = gearT = null;
                foreach (MonoBehaviour m in car.GetComponentsInChildren<MonoBehaviour>(true))
                    if (m != null && m.GetType().Name == "SteeringWheel") { steerT = m.transform; break; }
                foreach (Transform t in car.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "GearLever" || t.name == "gearlever") { gearT = t; break; }
                    // SORBET : Gearstick/Pivot (qui tourne), le pommeau au bout de sa tige
                    if (t.name == "Pivot" && t.parent != null && t.parent.name == "Gearstick") { gearT = t; break; }
                }
                if (steerT != null)
                {
                    Vector3 axis = (steerT.position - headBone.position).normalized;   // colonne : du conducteur vers le tableau de bord
                    Vector3 right = Vector3.ProjectOnPlane(car.right, axis).normalized, up = Vector3.Cross(axis, right).normalized;
                    if (Vector3.Dot(up, car.up) < 0f) up = -up;
                    const float R = 0.17f;
                    gripR = steerT.InverseTransformPoint(steerT.position + right * R * 0.9f + up * R * 0.42f);
                    gripL = steerT.InverseTransformPoint(steerT.position - right * R * 0.9f + up * R * 0.42f);
                }
                if (gearT != null) gearLast = gearT.localRotation;
                Transform shR = Bone("shoulder_right"), shL = Bone("shoulder_left");
                Log.Info("avatar " + Player.Name + " : mains au volant (" + (steerT != null ? steerT.name + " a " + Root.transform.InverseTransformPoint(steerT.position).ToString("F2") + ", prises D " + (shR != null ? (steerT.TransformPoint(gripR) - shR.position).magnitude.ToString("F2") : "?") + " m G " + (shL != null ? (steerT.TransformPoint(gripL) - shL.position).magnitude.ToString("F2") : "?") + " m de l'epaule, tete " + Root.transform.InverseTransformPoint(headBone.position).ToString("F2") : "volant introuvable") + ", levier " + (gearT != null ? "oui" : "non") + ")");
            }
            float now = Time.realtimeSinceStartup;
            if (gearT != null && Quaternion.Angle(gearT.localRotation, gearLast) > 1.5f) { gearLast = gearT.localRotation; gearUntil = Mathf.Max(gearUntil, now + 0.6f); }
            // Passage recu (CarVisuals) : la main part vers le pommeau avant que le levier ne bouge (0,15 s), y reste le temps
            // du passage, puis revient au volant.
            float shift = CarVisuals.LastShift(car);
            if (shift > gearSeen) { gearSeen = shift; gearUntil = Mathf.Max(gearUntil, shift + 0.75f); }
            Ease(ref wShift, now < gearUntil, Time.deltaTime * 9f);
            if (steerT != null)   // (jante hors de portee -- siege loin du volant : la pose du conducteur PNJ reste, bras pas etires)
            {
                Vector3 pr = steerT.TransformPoint(gripR), pl = steerT.TransformPoint(gripL);
                LeanToWheel(pr, pl);
                ArmTo(true, pr, (1f - wShift) * Reachable(true, pr));
                ArmTo(false, pl, Reachable(false, pl));
                Transform hr = Bone("hand_right"), hl = Bone("hand_left");
                handErrR = hr != null ? (hr.position - pr).magnitude : -1f; handErrL = hl != null ? (hl.position - pl).magnitude : -1f;
            }
            if (gearT != null && wShift > 0.001f) ArmTo(true, gearT.position + car.up * 0.1f, wShift);
        }

        float handErrR = -1f, handErrL = -1f, leanDeg;

        // ---------------------------------------------------------------- Jonnez (mobylette)
        // Demande de JD (10/10) : sur la Jonnez, l'avatar etait assis comme en voiture (jambes en avant a travers la moto, bras
        // ballants). Ici : buste un peu en avant, cuisses de part et d'autre de la selle, pieds au niveau du kick (repose-pieds),
        // mains aux poignees (gaz a droite : Throttle, embrayage a gauche : Clutch -- elles tournent avec le guidon). Kick (la
        // commande Kickstart rejouee, ReachFor) : le pied droit monte sur la pedale et l'enfonce (0,7 s). Passager (place
        // arriere, Seats) : meme assise, plus en arriere, les mains derriere lui sur les cotes du porte-bagages (rack).
        Transform vehicleT, motoCar, motoThrottle, motoClutch, motoCrank;
        Renderer motoSeat, motoRack;
        float kickAt = -10f, motoLog;
        Vector3 motoRaise;   // remontee du squelette sur la selle (monde), cette image
        void MotoFind(Transform car)
        {
            if (car == motoCar) return;
            motoCar = car; motoThrottle = motoClutch = motoCrank = null; motoSeat = motoRack = null;
            if (car == null) return;
            foreach (Transform t in car.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Throttle" && motoThrottle == null) motoThrottle = t;
                else if (t.name == "Clutch" && motoClutch == null) motoClutch = t;
                else if (t.name == "Crank" && motoCrank == null) motoCrank = t;
                else if (t.name == "seat" && motoSeat == null) motoSeat = t.GetComponent<Renderer>();
                else if (t.name == "rack" && motoRack == null && t.GetComponent<Renderer>() != null) motoRack = t.GetComponent<Renderer>();
            }
            Log.Info("avatar " + Player.Name + " : sur la Jonnez (gaz " + (motoThrottle != null) + ", embrayage " + (motoClutch != null) + ", kick " + (motoCrank != null) + ", selle " + (motoSeat != null) + ", porte-bagages " + (motoRack != null) + ")");
        }

        void MotoPose(bool rear)
        {
            Transform car = vehicleT;
            MotoFind(car);
            if (car == null) return;
            Transform r = Root.transform;
            // Bassin sur le dessus de la selle (retour de JD, 10/10 : « les fesses dans la moto ») : la tete suit la camera du
            // jeu, basse sur la Jonnez -- tout le squelette est remonte (comme Crouch le descend), avant les jambes et les bras.
            Transform pelvis = Bone("pelvis");
            if (pelvis != null && motoSeat != null)
            {
                // bassin pose sur la selle : au-dessus, a l'avant de la selle (conducteur) ou au bout (passager), sur l'axe
                Vector3 sc = car.InverseTransformPoint(motoSeat.bounds.center), pc = car.InverseTransformPoint(pelvis.position);
                float top = car.InverseTransformPoint(motoSeat.bounds.center + Vector3.up * motoSeat.bounds.extents.y).y + Config.GetFloat("Test", "MotoBassin", 0.09f);
                float half = car.InverseTransformVector(motoSeat.bounds.extents).z;
                Vector3 want = new Vector3(sc.x, top, sc.z + Mathf.Abs(half) * (rear ? -0.55f : 0.45f));
                Vector3 d = want - pc;
                if (d.sqrMagnitude < 2.25f) { motoRaise = car.TransformVector(d); anim.transform.position += motoRaise; }
            }
            // buste un peu en avant (conducteur), droit (passager)
            Turn(Bone("spine_middle"), 0f, Config.GetFloat("Test", "MotoBuste", rear ? 14f : 32f));   // (la pose de voiture est couchee en arriere)
            Turn(Bone("spine_upper"), 0f, Config.GetFloat("Test", "MotoBuste2", rear ? 8f : 14f));
            // pieds : repose-pieds au niveau du kick, de part et d'autre de la selle
            Vector3 mid = motoSeat != null ? motoSeat.bounds.center : car.position;
            Vector3 peg = motoCrank != null ? motoCrank.position : mid - car.up * 0.35f;
            Vector3 pegC = car.InverseTransformPoint(peg), midC = car.InverseTransformPoint(mid);
            Vector3 baseC = new Vector3(midC.x, pegC.y + 0.04f, pegC.z + (rear ? -0.28f : 0.04f));
            Vector3 footR = car.TransformPoint(baseC + new Vector3(0.18f, 0f, 0f)), footL = car.TransformPoint(baseC + new Vector3(-0.18f, 0f, 0f));
            float k = Time.realtimeSinceStartup - kickAt;
            if (!rear && k >= 0f && k < 0.7f && motoCrank != null)
            {
                // kick : pied leve au-dessus de la pedale (0-0,25 s), enfonce (0,25-0,5), revient (0,5-0,7)
                Vector3 up = motoCrank.position + car.up * 0.2f + car.right * 0.16f, down = motoCrank.position - car.up * 0.06f + car.right * 0.16f;
                footR = k < 0.25f ? Vector3.Lerp(footR, up, k / 0.25f) : k < 0.5f ? Vector3.Lerp(up, down, (k - 0.25f) / 0.25f) : Vector3.Lerp(down, footR, (k - 0.5f) / 0.2f);
            }
            Leg("thig_right", "knee_right", "ankle_right", footR);
            Leg("thig_left", "knee_left", "ankle_left", footL);
            if (!rear)
            {
                if (motoThrottle != null) ArmTo(true, motoThrottle.position, Reachable(true, motoThrottle.position));
                if (motoClutch != null) ArmTo(false, motoClutch.position, Reachable(false, motoClutch.position));
                MotoJournal(car);
                return;
            }
            // passager : mains derriere lui, sur les cotes du porte-bagages
            if (motoRack != null)
            {
                Vector3 c = motoRack.bounds.center;
                Vector3 hr = c + car.right * 0.13f + car.up * 0.03f, hl = c - car.right * 0.13f + car.up * 0.03f;
                ArmTo(true, hr, Reachable(true, hr));
                ArmTo(false, hl, Reachable(false, hl));
            }
            else { ArmDown("shoulder_right", "hand_right", 1f, 1f); ArmDown("shoulder_left", "hand_left", -1f, 1f); }
            MotoJournal(car);
        }
        // Essais ([Test] JournalMoto=1) : os et pieces de la Jonnez dans le repere de la moto, toutes les 3 s.
        void MotoJournal(Transform car)
        {
            Transform r = Root.transform;
            if (Time.realtimeSinceStartup >= motoLog && Config.GetInt("Test", "JournalMoto", 0) != 0)
            {
                motoLog = Time.realtimeSinceStartup + 3f;
                var sb = new System.Text.StringBuilder("moto " + Player.Name + " (repere moto) : racine " + car.InverseTransformPoint(r.position).ToString("F2") + " yaw racine/moto " + Mathf.DeltaAngle(car.eulerAngles.y, r.eulerAngles.y).ToString("F0"));
                if (motoSeat != null) sb.Append(" selle ").Append(car.InverseTransformPoint(motoSeat.bounds.center).ToString("F2")).Append(" taille ").Append(motoSeat.bounds.size.ToString("F2"));
                if (motoThrottle != null) sb.Append(" gaz ").Append(car.InverseTransformPoint(motoThrottle.position).ToString("F2"));
                if (motoClutch != null) sb.Append(" embr ").Append(car.InverseTransformPoint(motoClutch.position).ToString("F2"));
                if (motoCrank != null) sb.Append(" kick ").Append(car.InverseTransformPoint(motoCrank.position).ToString("F2"));
                foreach (string bn in new[] { "pelvis", "head", "hand_right", "hand_left", "knee_right", "ankle_right", "ankle_left" })
                { Transform bt = Bone(bn); if (bt != null) sb.Append(' ').Append(bn).Append(car.InverseTransformPoint(bt.position).ToString("F2")); }
                Log.Info(sb.ToString());
            }
        }
        // Assis sur l'assise du siege (demande de JD, 10/10, GIFU : « le corps enfonce dans le fauteuil, remonte-le et
        // adapte les jambes ») : le bassin pose au-dessus du dessus de l'assise (collisionneur Colliders/Cabin/Seats le plus
        // proche, le plus bas des deux : assise, pas dossier), contre le dossier ; tout le squelette deplace (comme la
        // Jonnez, hors de l'ancrage aux yeux), les pieds laisses au plancher (jambes refaites). Conducteur et passager avant.
        Transform seatCar; readonly List<Collider> seatCols = new List<Collider>();
        void SeatFit(bool rear)
        {
            if (carName == null || !carName.StartsWith("GIFU") || vehicleT == null || Config.GetInt("Test", "SansSiege", 0) != 0) return;   // (essais : SansSiege=1, comme avant)
            // (passager avant : enfonce de 30 cm dans le fauteuil sans ca -- assis comme le conducteur, dos arrondi, mains sur
            // les cuisses ; la couchette n'a pas de « Seats » : rien a faire)
            Transform car = vehicleT, pelvis = Bone("pelvis");
            if (pelvis == null) return;
            if (seatCar != car)
            {
                seatCar = car; seatCols.Clear();
                foreach (Collider c in car.GetComponentsInChildren<Collider>(true)) if (!c.isTrigger && c.name == "Seats") seatCols.Add(c);
            }
            Vector3 pl = car.InverseTransformPoint(pelvis.position);
            Collider cushion = null; float best = float.MaxValue;
            foreach (Collider c in seatCols)
            {
                if (c == null) continue;
                Vector3 cc = car.InverseTransformPoint(c.bounds.center);
                if (Mathf.Abs(cc.x - pl.x) > 0.5f) continue;
                float score = cc.y;   // (l'assise : la plus basse des deux pieces de ce cote)
                if (score < best) { best = score; cushion = c; }
            }
            if (cushion == null) return;
            Vector3 a = car.InverseTransformPoint(cushion.bounds.min), b = car.InverseTransformPoint(cushion.bounds.max);
            float top = Mathf.Max(a.y, b.y), back = Mathf.Min(a.z, b.z);
            if (pl.z < back - 0.4f) return;   // (assis sur la couchette, derriere les sieges)
            Vector3 want = new Vector3(pl.x, top + Config.GetFloat("Test", "SiegeBassin", 0.02f), back + Config.GetFloat("Test", "SiegeRecul", 0.12f));
            Vector3 d = want - pl; d.x = 0f;
            if (Config.GetInt("Test", "JournalSiege", 0) != 0 && Time.frameCount % 300 < 6) Log.Info("siege : image " + Time.frameCount + " colonne " + (Bone("spine_middle") != null ? Bone("spine_middle").localEulerAngles.ToString("F1") : "?") + " racine " + vehicleT.InverseTransformPoint(Root.transform.position).ToString("F2") + " bassin " + pl.ToString("F2") + " voulu " + want.ToString("F2") + " ecart " + d.ToString("F2") + (d.sqrMagnitude > 0.64f ? " TROP LOIN" : ""));
            if (d.sqrMagnitude > 0.64f) return;   // (plus de 80 cm : pas ce siege)
            Transform ar = Bone("ankle_right"), al = Bone("ankle_left");
            motoRaise = car.TransformVector(d);
            anim.transform.position += motoRaise;
            seatFitted = true;
            // (dos contre le dossier : buste incline apres la mesure des yeux au repos, dans le corps de la pose)
            // Cuisses sur l'assise, genoux plies, pieds aux pedales : chaque pied devant et sous sa hanche
            float fwd = Config.GetFloat("Test", "SiegePieds", 0.55f), down = Config.GetFloat("Test", "SiegeChute", 0.5f);
            Transform hr = Bone("thig_right"), hl = Bone("thig_left");
            if (ar != null && hr != null) Leg("thig_right", "knee_right", "ankle_right", hr.position + car.forward * fwd - car.up * down + car.right * 0.04f);
            if (al != null && hl != null) Leg("thig_left", "knee_left", "ankle_left", hl.position + car.forward * fwd - car.up * down - car.right * 0.04f);
        }

        // [Test] JournalSiege=1 : os de l'avatar assis et sieges du vehicule, dans le repere du vehicule, toutes les 3 s.
        float seatLog;
        void SeatLog()
        {
            if (Time.realtimeSinceStartup < seatLog || Config.GetInt("Test", "JournalSiege", 0) == 0 || vehicleT == null) return;
            seatLog = Time.realtimeSinceStartup + 3f;
            Transform car = vehicleT;
            var sb = new System.Text.StringBuilder("siege " + Player.Name + " dans " + carName + " : racine" + car.InverseTransformPoint(Root.transform.position).ToString("F2") + " yeux-repos" + eyesRest.ToString("F2"));
            foreach (string bn in new[] { "pelvis", "thig_right", "knee_right", "ankle_right", "spine_upper", "head" })
            { Transform bt = Bone(bn); if (bt != null) sb.Append(' ').Append(bn).Append(car.InverseTransformPoint(bt.position).ToString("F2")); }
            Vector3 dh; if (Seats.DriverHead(car, out dh)) sb.Append(" tete-conducteur").Append(dh.ToString("F2"));
            sb.Append(" camera-du-joueur").Append(car.InverseTransformPoint(Player.State.Head).ToString("F2"));
            if (headBone != null) sb.Append(" yeux-avatar").Append(car.InverseTransformPoint(headBone.position + Root.transform.rotation * EyeOffset).ToString("F2"));
            foreach (Renderer r in car.GetComponentsInChildren<Renderer>())
            {
                string n = r.name.ToLowerInvariant();
                if (!n.Contains("seat") && !n.Contains("chair") && !n.Contains("pedal")) continue;
                Bounds bb = r.bounds;
                sb.Append(" | ").Append(r.name).Append(" centre").Append(car.InverseTransformPoint(bb.center).ToString("F2")).Append(" haut ").Append(car.InverseTransformPoint(bb.center + Vector3.up * bb.extents.y).y.ToString("F2"));
            }
            foreach (Collider c in car.GetComponentsInChildren<Collider>())
            {
                string n = c.name.ToLowerInvariant();
                if (!n.Contains("seat") || c.isTrigger) continue;
                Bounds bb = c.bounds;
                sb.Append(" | col ").Append(c.name).Append(" min").Append(car.InverseTransformPoint(bb.min).ToString("F2")).Append(" max").Append(car.InverseTransformPoint(bb.max).ToString("F2"));
            }
            Log.Info(sb.ToString());
        }

        // Essais : mains au volant (ecart main - prise), penche vers le volant, levier trouve.
        public string HandsState()
        {
            return Player.Name + " : volant " + (steerT != null ? steerT.name : "-") + ", main D a " + handErrR.ToString("F2") + " m de sa prise, G a " + handErrL.ToString("F2")
                   + " m, penche " + leanDeg.ToString("F0") + " deg, levier " + (gearT != null ? gearT.parent.name + "/" + gearT.name : "-") + ", main au levier " + wShift.ToString("F2");
        }

        // Dosage selon la portee : 1 jusqu'a 105 % de la longueur du bras, 0 au-dela de 130 % (le bras, tendu, s'arrete a
        // sa longueur : la main au bord de la jante).
        bool seatFitted;   // (assis contre le dossier : bras tendus jusqu'au volant, dessin de JD)
        float Reachable(bool right, Vector3 at)
        {
            if (seatFitted) return 1f;
            string s = right ? "_right" : "_left";
            Transform sh = Bone("shoulder" + s), el = Bone("arm" + s), ha = Bone("hand" + s);
            if (sh == null || el == null || ha == null) return 0f;
            float len = ArmLen(sh, el, ha);
            return Mathf.Clamp01((1.3f * len - (at - sh.position).magnitude) / (0.25f * len));
        }
        static float ArmLen(Transform sh, Transform el, Transform ha) { return (el.position - sh.position).magnitude + (ha.position - el.position).magnitude; }

        // Volant loin des epaules (SORBET : 0,9 m pour des bras de 0,6 -- retour de JD, 07/10 : pas de mains au volant) :
        // le buste se penche vers lui (spine_middle et spine_upper, autour de la droite de l'avatar), jusqu'a 28 degres,
        // pour que la prise la plus loin tombe a 95 % de la longueur du bras.
        void LeanToWheel(Vector3 pr, Vector3 pl)
        {
            Transform shR = Bone("shoulder_right"), elR = Bone("arm_right"), haR = Bone("hand_right"), sm = Bone("spine_middle");
            if (shR == null || elR == null || haR == null || sm == null) return;
            Transform shL = Bone("shoulder_left");
            float len = ArmLen(shR, elR, haR);
            float far = Mathf.Max((pr - shR.position).magnitude, shL != null ? (pl - shL.position).magnitude : 0f);
            float excess = far - 0.95f * len;
            leanDeg = 0f;
            if (excess <= 0f) return;
            float h = Mathf.Max(0.2f, (shR.position - sm.position).magnitude);
            float ang = Mathf.Min(28f, Mathf.Asin(Mathf.Clamp01(excess / h)) * Mathf.Rad2Deg * 1.3f);
            leanDeg = ang;
            foreach (string sp in new[] { "spine_middle", "spine_upper" })
            {
                Transform b = Bone(sp);
                if (b != null) b.rotation = Quaternion.AngleAxis(ang * 0.5f, Root.transform.right) * b.rotation;
            }
        }

        // Main droite vers une commande que ce joueur vient d'actionner (bouton, clef, molette, interrupteur, porte) : un
        // vrai appui (demande d'un joueur, 08/10) -- le bras se tend (0,18 s), le doigt enfonce de 4 cm vers la commande,
        // tient, puis revient (0,7 s en tout). Visee : le milieu de ce qu'on voit de la commande, pas le pivot de son automate.
        public void ReachFor(Vector3 at)
        {
            // Main du cote de la commande (passager a droite : tableau de bord a sa gauche -> main gauche ; avant : toujours
            // la droite, bras en travers du corps).
            reachRight = Root == null || Root.transform.InverseTransformPoint(at).x >= -0.08f;
            Transform sh = Bone(reachRight ? "shoulder_right" : "shoulder_left");
            if (sh == null || (at - sh.position).sqrMagnitude > 1.2f * 1.2f) return;   // trop loin : pas lui
            reachAt = at;
            reachStart = Time.realtimeSinceStartup;
            reachUntil = reachStart + 0.7f;
            if (pressLogs++ < 5) Log.Info("avatar " + (Player != null ? Player.Name : "?") + " : appuie a " + (at - sh.position).magnitude.ToString("F2") + " m de l'epaule");
        }
        public void ReachFor(Transform t)
        {
            if (t == null) return;
            if (t.name == "Kickstart") { kickAt = Time.realtimeSinceStartup; return; }   // (Jonnez : le pied sur le kick, MotoPose)
            Vector3 at = t.position;
            Renderer best = null;
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>())
                if (r.enabled && (best == null || r.bounds.size.sqrMagnitude < best.bounds.size.sqrMagnitude)) best = r;
            if (best != null && best.bounds.size.magnitude < 1.5f) at = best.bounds.center;
            else { Collider c = t.GetComponent<Collider>(); if (c != null) at = c.bounds.center; }
            ReachFor(at);
        }
        float reachStart;
        bool reachRight = true;
        static int pressLogs;
        void ReachPose()
        {
            float e = Time.realtimeSinceStartup - reachStart;
            if (e < 0f || e > 0.7f) { wReach = 0f; return; }
            float w = e < 0.18f ? e / 0.18f : e > 0.45f ? 1f - (e - 0.45f) / 0.25f : 1f;
            wReach = Mathf.Clamp01(w * w * (3f - 2f * w));
            Transform sh = Bone(reachRight ? "shoulder_right" : "shoulder_left");
            Vector3 dir = sh != null ? (reachAt - sh.position).normalized : Root.transform.forward;
            float poke = e > 0.15f && e < 0.45f ? Mathf.Sin(Mathf.PI * (e - 0.15f) / 0.3f) * 0.04f : 0f;
            if (wReach > 0.001f) ArmTo(reachRight, reachAt + dir * (poke - 0.02f), wReach * Reachable(reachRight, reachAt));
        }
        // Bouche (devant et sous l'os de la tete) ; sens de la bouteille quand on boit : du cul vers le goulot, vers le
        // visage et vers le bas (le cul plus haut que le goulot).
        Vector3 Mouth() { Transform r = Root.transform; return headBone.position + r.forward * 0.11f - r.up * 0.09f; }
        Vector3 DrinkDir() { Transform r = Root.transform; return (-r.forward * 0.75f - r.up * 0.62f).normalized; }

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
            // Boire, fumer : la main va VRAIMENT a la bouche (retour d'un joueur, 07/10) -- les clips des PNJ ne la montent
            // pas jusque-la. Boire : main droite sous la bouche tant qu'il boit (la bouteille bascule, goulot aux levres,
            // PlaceDrink) ; fumer : main gauche a la bouche quand il tire (smokeRaise), cigarette aux levres.
            Ease(ref wDrink, (f & PlayerSync.F_Drink) != 0 && st.Drink > 0, Time.deltaTime * 3f);
            if (headBone != null)
            {
                Vector3 mouth = Mouth();
                // la main tient la bouteille en son milieu, la bouteille du goulot (aux levres) vers l'avant et le haut
                ArmTo(true, mouth - DrinkDir() * drinkHalf * 1.1f - r.up * 0.03f + r.right * 0.02f, wDrink);
                if ((f & PlayerSync.F_Smoke) != 0) ArmTo(false, mouth - r.up * 0.03f - r.right * 0.05f, smokeRaise);
            }
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
            ReachPose();
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

        int faceGen;
        public void Apply(PlayerInfo pi)
        {
            PlayerState st = pi.State;
            int f = st.Flags;
            int cloth = f & (PlayerSync.F_Jacket | PlayerSync.F_Coverall);
            if ((skin != pi.Skin || cloth != clothFlags || faceGen != CustomFace.Generation) && body != null)
            {
                faceGen = CustomFace.Generation;   // (visage importe recu entre-temps : repris)
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
                vehicleT = pCar;
                Root.transform.rotation = pCar.rotation;
                leanOff = Vector3.zero;
                pos = pCar.TransformPoint(pHead) - pCar.rotation * eyesRest;
                // Couchette de la GIFU : 67 cm seulement sous le toit, la tete (camera) juste dessous ; assis droit, le corps
                // s'enfoncait de ~22 cm dans la couchette (retour d'un joueur, 09/10). Assis dessus, voute en avant : la tete
                // reste sous le toit.
                if (pName != null && pName.StartsWith("GIFU") && Seats.RemoteSeatIndex(pi.Id) >= 1)
                {
                    pos += pCar.up * Config.GetFloat("Test", "CouchetteLeve", 0.22f) + pCar.forward * Config.GetFloat("Test", "CouchetteAvance", 0.1f);   // (dos contre la paroi, pas dedans)
                    leanOff = new Vector3(0f, 0f, Config.GetFloat("Test", "CouchetteVoute", 0.5f));
                }
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
                vehicleT = carT;
                // (tete envoyee avec la voiture, repere voiture : exacte ; sinon celle de PlayerSync, decalee en roulant)
                Vector3 sentHead;
                bool exact = VehicleSync.RemoteHeadLocal(pi.Id, out sentHead);
                Vector3 headLocal = exact ? sentHead : carT != null ? carT.InverseTransformPoint(seatPos) : Vector3.zero;
                // Ancre : la place du conducteur de la voiture (DriverHeadPivot), plus le premier echantillon de la tete --
                // la tete (PlayerSync) et la voiture (VehicleSync) arrivent par deux messages pas synchronises : a 90 km/h
                // l'ecart d'appariement fait plus d'un metre, l'ancre prise dessus decalait tout le corps (avatar qui
                // « saute » en avant en conduisant, retour d'un joueur, 08/10). L'ecart a la tete ne fait plus que pencher
                // le buste, lisse, et de moins en moins avec la vitesse (rien au-dela de ~40 km/h).
                Vector3 rest;
                if (!anchorSet || anchorCar != carName)
                {
                    seatAnchor = Seats.DriverHead(carT, out rest) ? rest : headLocal;
                    anchorSet = true; anchorCar = carName; leanSmooth = Vector3.zero;
                }
                Vector3 off = headLocal - seatAnchor;
                float speed = VehicleSync.RemoteSpeed(pi.Id);
                float maxLean = exact ? 0.45f : Mathf.Lerp(0.6f, 0f, (speed - 2f) / 9f);
                leanSmooth = Vector3.Lerp(leanSmooth, Vector3.ClampMagnitude(off, maxLean), Time.deltaTime * 6f);
                leanOff = leanSmooth;
                if (Config.GetInt("Test", "SuivreConducteur", 0) != 0 && Time.frameCount % 30 == 0)
                    Log.Info("autotest : conducteur " + Player.Name + " penche " + leanSmooth.magnitude.ToString("F2") + " m (ecart " + off.magnitude.ToString("F2") + ", ancien calcul " + (carT != null ? (carT.InverseTransformPoint(seatPos) - seatAnchor).magnitude.ToString("F2") : "?") + ", " + (exact ? "tete envoyee avec la voiture" : "tete de PlayerSync") + ", " + (speed * 3.6f).ToString("F0") + " km/h)");
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
            Blanket(sleep);
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
            // Dans un vehicule : la place dans la voiture, reposee en fin d'image (LatePose) sur la voiture deja deplacee.
            riding = inCar && vehicleT != null;
            if (riding) { rideLocalPos = vehicleT.InverseTransformPoint(Root.transform.position); rideLocalRot = Quaternion.Inverse(vehicleT.rotation) * Root.transform.rotation; }
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
        Vector3 leanSmooth;   // conducteur : penche du buste, lisse
        float gearSeen = -1f; // dernier passage de vitesse recu (CarVisuals.LastShift) deja suivi par la main
        Vector3 drinkAxis = Vector3.up;   // axe long du modele, du cul vers le goulot (repere de l'objet)
        float drinkHalf = 0.12f;           // demi-longueur une fois a l'echelle (m)
        float drinkLogAt;

        // Boisson par boisson (verifie en captures, essai BoissonDefile / CaptureBoisson, 08/10) : taille une fois en main
        // (plus grande dimension, m) et sens du goulot quand les sommets le donnent faux (+1 : goulot vers +axe trouve,
        // -1 : inverse, 0 : d'apres les sommets). La canette EnergyDrink etait a l'envers et doublee (11 cm ramenes a 24).
        static readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.KeyValuePair<float, int>> DrinkTable = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.KeyValuePair<float, int>> {
            { "BeerBottle", new System.Collections.Generic.KeyValuePair<float, int>(0.23f, 0) }, { "BoozeBottle", new System.Collections.Generic.KeyValuePair<float, int>(0.27f, 0) },
            { "SpiritBottle", new System.Collections.Generic.KeyValuePair<float, int>(0.25f, 0) }, { "Milk", new System.Collections.Generic.KeyValuePair<float, int>(0.22f, 0) },
            { "MilkGlass", new System.Collections.Generic.KeyValuePair<float, int>(0.11f, 0) }, { "SodaPSK", new System.Collections.Generic.KeyValuePair<float, int>(0.22f, 0) },
            { "EnergyDrink", new System.Collections.Generic.KeyValuePair<float, int>(0.12f, -1) }, { "ShotGlass", new System.Collections.Generic.KeyValuePair<float, int>(0.06f, 0) },
            { "Coffee", new System.Collections.Generic.KeyValuePair<float, int>(0.10f, 0) }, { "CoffeePaper", new System.Collections.Generic.KeyValuePair<float, int>(0.11f, 0) },
            { "CoffeeGranny", new System.Collections.Generic.KeyValuePair<float, int>(0.09f, 0) }, { "HandJuice", new System.Collections.Generic.KeyValuePair<float, int>(0.22f, 0) },
            { "HandMilk", new System.Collections.Generic.KeyValuePair<float, int>(0.22f, 0) }, { "HandCoffeeHome", new System.Collections.Generic.KeyValuePair<float, int>(0.10f, 0) } };

        void DrinkInHand(int i)
        {
            if (i == drinkIdx) { if (drinkGo != null && drinkGo.activeSelf != (i > 0)) drinkGo.SetActive(i > 0); return; }
            drinkIdx = i;
            if (drinkGo != null) { Object.Destroy(drinkGo); drinkGo = null; }
            if (i <= 0) return;
            drinkGo = Drinks.Model(i, Root.transform);
            if (drinkGo == null)   // (main du joueur local pas encore prete au chargement : on reessaie, sinon elle restait invisible)
            {
                if (Time.realtimeSinceStartup >= drinkLogAt) { drinkLogAt = Time.realtimeSinceStartup + 30f; Log.Warn("avatar " + Player.Name + " : boisson " + (i < Drinks.Names.Length ? Drinks.Names[i] : i.ToString()) + " pas encore disponible (" + Drinks.HandChildren() + "), nouvel essai"); }
                drinkIdx = -1;
                return;
            }
            // Taille : la plus grande dimension de ses rendus ramenee a celle du tableau (sinon 24 cm, ou 11 cm pour tasses et verres).
            Bounds b = new Bounds(drinkGo.transform.position, Vector3.zero);
            bool any = false;
            foreach (Renderer r in drinkGo.GetComponentsInChildren<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            string n = Drinks.Names[i];
            System.Collections.Generic.KeyValuePair<float, int> row;
            bool known = DrinkTable.TryGetValue(n, out row);
            float want = known ? row.Key : n.Contains("Coffee") || n.Contains("Glass") ? 0.11f : 0.24f;
            float size = any ? Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) : 0f;
            if (size > 0.01f) drinkGo.transform.localScale *= want / size;
            drinkHalf = want / 2f;
            DrinkAxis();
            if (known && row.Value < 0) drinkAxis = -drinkAxis;
            Log.Info("avatar " + Player.Name + " : boit (" + n + ", " + (size > 0f ? (size * 100f).ToString("F0") + " cm ramenes a " + (want * 100f).ToString("F0") : "taille ?") + ")");
            if (Config.GetInt("Test", "CaptureBoisson", 0) != 0) Autotest.CaptureSoon("boisson-" + n, 3f);
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
            float t = headBone != null ? Mathf.Max(Mathf.Clamp01(1f - (dist - 0.12f) / 0.20f), wDrink) : 0f;   // (en buvant : basculee, goulot aux levres)
            Vector3 toMouth = dist > 0.01f ? (mouth - fist) / dist : -fwd;
            Vector3 dir = Vector3.Slerp(up, (toMouth - up * 0.45f).normalized, t).normalized;   // du cul vers le goulot
            if (Time.realtimeSinceStartup >= drinkLogAt && Config.GetInt("Test", "JournalBoisson", 0) != 0)
            {
                drinkLogAt = Time.realtimeSinceStartup + 1f;
                Log.Info("boisson : poing-bouche " + Vector3.Distance(fist, mouth).ToString("F2") + " m, bascule " + t.ToString("F2") + ", poing " + Root.transform.InverseTransformPoint(fist).ToString("F2")
                         + ", tete " + (headBone != null ? Root.transform.InverseTransformPoint(headBone.position).ToString("F2") : "?") + ", axe " + drinkAxis);
            }
            if (wDrink > 0.001f)   // en buvant : goulot aux levres, cul releve vers l'avant (la main suit, GesturePose)
            {
                Vector3 dd = DrinkDir();
                dir = Vector3.Slerp(dir, dd, wDrink).normalized;
            }
            drinkGo.transform.rotation = Quaternion.FromToRotation(drinkAxis, dir);
            // Centre de la bouteille sur son axe, depuis le poing : un peu au-dessus en la tenant ; en buvant, de sorte que le
            // goulot (centre + demi-longueur) touche la bouche, sans que la main quitte la bouteille.
            float along = Mathf.Lerp(0.2f, Mathf.Clamp(dist / drinkHalf - 0.95f, -0.6f, 0.6f), t);
            Vector3 target = fist + dir * drinkHalf * along;
            if (wDrink > 0.001f && headBone != null) target = Vector3.Lerp(target, Mouth() - dir * drinkHalf, wDrink);   // goulot sur la bouche
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
            Looks.Look look = Looks.Parse(skin);
            Material m = FindMaterial(look.Shirt) ?? baseMat ?? body.sharedMaterial;
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
            // haut, pantalon, visage (sous-maillages du corps ; champ vide : ceux du modele)
            Material[] arr = body.sharedMaterials;
            if (arr.Length > 0 && m != null) arr[0] = m;
            if (arr.Length > 1) arr[1] = FindMaterial(look.Pants) ?? (defMats != null && defMats.Length > 1 ? defMats[1] : arr[1]);
            if (arr.Length > 2) arr[2] = FindMaterial(look.Face) ?? (defMats != null && defMats.Length > 2 ? defMats[2] : arr[2]);
            body.sharedMaterials = arr;
            Accessories(look);
        }

        // Couverture sur le dormeur (demande d'un joueur, 07/10) : un drap epais des pieds a la poitrine, dans le repere de
        // la racine (couche sur le dos : +y vers la tete, +z vers le haut), matiere de literie du jeu.
        GameObject blanket;
        static readonly string[] BlanketMats = { "bed_blanket", "blanket", "bed_cover", "bed_white", "fabric_flower3", "curtain4" };
        void Blanket(bool on)
        {
            if (on && blanket == null && Root != null)
            {
                Material mat = null;
                foreach (string n in BlanketMats) { mat = FindMaterial(n); if (mat != null) break; }
                // Drap drape (avant : un cube, retour d'un joueur 08/10) : bombe sur le corps, retombant sur les cotes,
                // repli a la poitrine ; les deux faces (vu de cote, pas de trou).
                blanket = new GameObject("MWCoop-Couverture");
                blanket.layer = Root.layer;
                blanket.transform.parent = Root.transform;
                blanket.transform.localPosition = new Vector3(0f, -0.12f, 0f);
                blanket.transform.localRotation = Quaternion.identity;
                blanket.transform.localScale = Vector3.one;
                blanket.AddComponent<MeshFilter>().sharedMesh = BlanketMesh();
                var mr = blanket.AddComponent<MeshRenderer>();
                if (mat != null) mr.sharedMaterial = mat;
                else mr.sharedMaterial = new Material(Shader.Find("Diffuse")) { color = new Color(0.55f, 0.6f, 0.68f) };
                Log.Info("avatar " + (Player != null ? Player.Name : "?") + " : couverture (" + (mat != null ? mat.name : "matiere par defaut") + ")");
            }
            if (blanket != null && blanket.activeSelf != on) blanket.SetActive(on);
        }

        // Maillage de la couverture (repere de la racine couchee : x en travers, y des pieds vers la tete, z vers le haut).
        static Mesh blanketMesh;
        static Mesh BlanketMesh()
        {
            if (blanketMesh != null) return blanketMesh;
            const int nx = 14, ny = 18;
            const float W = 0.92f, L = 1.34f;
            int n = (nx + 1) * (ny + 1);
            var v = new Vector3[n * 2];   // (dessus, puis dessous : sommets separes, normales opposees)
            var uv = new Vector2[n * 2];
            for (int j = 0; j <= ny; j++)
                for (int i = 0; i <= nx; i++)
                {
                    float x = (i / (float)nx - 0.5f) * W, y = j / (float)ny * L;
                    float a = Mathf.Clamp01(Mathf.Abs(x) / (W * 0.5f));
                    float body = Mathf.Pow(Mathf.Clamp01(1f - a * a), 0.6f);            // bombe sur le corps
                    float z = -0.03f + 0.19f * body;
                    z += 0.05f * Mathf.Clamp01(1f - Mathf.Abs(y - 0.16f) / 0.14f) * body;   // pieds (orteils dresses)
                    if (y > L - 0.16f) z += 0.02f;                                        // repli a la poitrine
                    z -= 0.015f * Mathf.Sin(y * 9f) * a;                                  // plis sur les cotes
                    v[j * (nx + 1) + i] = v[n + j * (nx + 1) + i] = new Vector3(x, y, z);
                    uv[j * (nx + 1) + i] = uv[n + j * (nx + 1) + i] = new Vector2(i / (float)nx, j / (float)ny * 1.4f);
                }
            var tri = new System.Collections.Generic.List<int>();
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    int a0 = j * (nx + 1) + i, a1 = a0 + 1, b0 = a0 + nx + 1, b1 = b0 + 1;
                    tri.AddRange(new[] { a0, b0, a1, a1, b0, b1 });   // dessus
                    tri.AddRange(new[] { n + a0, n + a1, n + b0, n + a1, n + b1, n + b0 });   // dessous
                }
            blanketMesh = new Mesh { name = "MWCoop-Couverture", vertices = v, uv = uv, triangles = tri.ToArray() };
            blanketMesh.RecalculateNormals();
            blanketMesh.RecalculateBounds();
            return blanketMesh;
        }

        // Chapeau, lunettes, cheveux : copies des objets des PNJ, sur l'os de la tete (pose relevee chez le PNJ).
        readonly GameObject[] acc = new GameObject[3];
        readonly string[] accKey = { "", "", "" };
        void Accessories(Looks.Look l)
        {
            string[] want = { l.Hat ?? "", l.Glasses ?? "", l.Hair ?? "" };
            for (int k = 0; k < 3; k++)
            {
                if (want[k] == accKey[k]) continue;
                if (acc[k] != null) Object.Destroy(acc[k]);
                acc[k] = null;
                accKey[k] = want[k];
                Looks.AccInfo ai = Looks.Acc(want[k]);
                if (ai == null || headBone == null) continue;
                var g = new GameObject("MWCoop-Accessoire-" + ai.Key);
                g.layer = headBone.gameObject.layer;
                g.transform.parent = headBone;
                g.transform.localPosition = ai.Pos;
                g.transform.localRotation = ai.Rot;
                g.transform.localScale = ai.Scale;
                Vector3 hd = HeadDelta;
                if (hd.sqrMagnitude > 1e-6f) g.transform.position += hd;   // (tete de ce corps plus basse ou plus haute que l'os)
                g.AddComponent<MeshFilter>().sharedMesh = ai.Mesh;
                g.AddComponent<MeshRenderer>().sharedMaterials = ai.Mats;
                acc[k] = g;
            }
            if (acc[0] != null) acc[0].SetActive(helmet == null || !helmet.activeSelf);
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
            if (acc[0] != null && acc[0].activeSelf == (helmet != null && helmet.activeSelf)) acc[0].SetActive(!(helmet != null && helmet.activeSelf));   // (chapeau sous le casque : cache)
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
