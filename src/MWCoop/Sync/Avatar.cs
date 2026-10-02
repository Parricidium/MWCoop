using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Corps visible d'un joueur distant : copie du personnage 'Char' d'un marcheur du jeu
    // (HUMANS/Randomizer/Walkers/*/Pivot/Char : bodymesh + skeleton anime), debarrasse de sa
    // logique, avec le materiau (« apparence ») choisi par le joueur. Clips : fat_walk, fat_standing.
    public class Avatar
    {
        static GameObject template;          // copie inactive, sans logique
        static Vector3 charOffset;           // position de Char par rapport aux pieds du marcheur
        static Quaternion charRotation = Quaternion.identity;
        static Vector3 charScale = Vector3.one;    // echelle globale de Char (ses parents sont mis a l'echelle)
        static Dictionary<string, Material> materials;

        public GameObject Root;
        public PlayerInfo Player;
        Animation anim;
        SkinnedMeshRenderer body;
        string skin;
        Vector3 pos;
        float yaw;
        bool placed;
        float nextDiag;

        public Vector3 HeadPosition { get { return Root.transform.position + Vector3.up * 1.95f; } }

        public static void ResetTemplate()
        {
            if (template != null) Object.Destroy(template);
            template = null;
            materials = null;
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
            if (a.anim != null) a.anim.cullingType = AnimationCullingType.AlwaysAnimate;
            a.body = ch.GetComponentInChildren<SkinnedMeshRenderer>();
            if (a.body != null) { a.body.updateWhenOffscreen = true; a.body.enabled = true; }
            Log.Info("avatar cree pour " + pi.Name + " (#" + pi.Id + ")");
            return a;
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
            float crouch = (st.Flags & 1) != 0 ? 0.6f : 1f;
            Root.transform.localScale = new Vector3(1f, crouch, 1f);
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
                bool moving = st.Speed > 0.3f;
                string clip = moving ? "fat_walk" : "fat_standing";
                if (anim[clip] != null)
                {
                    anim[clip].speed = moving ? Mathf.Clamp(st.Speed / 1.4f, 0.6f, 2.5f) : 1f;
                    if (!anim.IsPlaying(clip)) anim.CrossFade(clip, 0.25f);
                }
            }
        }

        public void Destroy()
        {
            if (Root != null) Object.Destroy(Root);
            Root = null;
        }
    }
}
