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

        public Vector3 HeadPosition { get { return Root.transform.position + Vector3.up * 1.95f; } }

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
            if (a.anim != null) { a.anim.cullingType = AnimationCullingType.AlwaysAnimate; a.AddClips(); }
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
            if (!placed) { pos = st.Feet; yaw = st.Yaw; placed = true; }
            // Lissage : rattrape l'etat recu (20/s) sans a-coups ; teleportation au-dela de 8 m.
            float k = 1f - Mathf.Exp(-12f * Time.deltaTime);
            if ((st.Feet - pos).sqrMagnitude > 64f) pos = st.Feet; else pos = Vector3.Lerp(pos, st.Feet, k);
            yaw = Mathf.LerpAngle(yaw, st.Yaw, k);
            Root.transform.position = pos;
            Root.transform.rotation = Quaternion.Euler(0, yaw, 0);
            int f = st.Flags;
            // Accroupi : pose assise sans siege, un peu plus bas ; assis (vehicule, chaise) : pose assise.
            bool sit = (f & (PlayerSync.F_Seated | PlayerSync.F_Crouch | PlayerSync.F_Sleep)) != 0;
            Root.transform.localScale = Vector3.one;
            if (Time.realtimeSinceStartup >= nextDiag && body != null)
            {
                nextDiag = Time.realtimeSinceStartup + 10f;
                Log.Info("avatar " + Player.Name + " : pieds " + pos.ToString("F2") + ", rendu " + body.enabled
                         + "/" + body.gameObject.activeInHierarchy + ", visible " + body.isVisible
                         + ", boite " + body.bounds.min.ToString("F2") + " - " + body.bounds.max.ToString("F2")
                         + ", couche " + body.gameObject.layer + ", anim " + (anim != null && anim.isPlaying));
            }
            if (anim != null)
            {
                bool moving = st.Speed > 0.3f && !sit;
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
