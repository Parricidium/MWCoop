using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // PNJ du monde (vendeurs, caissieres, clients, Teimo, Fleetari, boulots...), vus pareil par tous.
    // L'hote envoie 5 fois par seconde, pour les PNJ actifs chez lui a moins de 80 m d'un invite : pose
    // du corps visible (objet Char), clip d'animation joue et son instant, et les objets qu'il tient
    // (telephone, assiette...). Chez l'invite, seul le CORPS VISIBLE suit (apres l'animation du jeu) : la
    // logique locale du PNJ continue de tourner (la caissiere vient toujours servir l'invite). Sans
    // nouvelles de l'hote depuis 1 s (PNJ loin de lui, eteint chez lui), l'invite revoit les siens.
    // Hors circulation et passants (Traffic) et courses.
    public static class Npcs
    {
        class Npc
        {
            public int Id; public Transform Char; public Animation Anim; public GameObject[] Props;
            public Vector3 RestPos; public Quaternion RestRot;
            // recu de l'hote
            public bool Held; public float HeldAt; public Vector3 Pos; public Quaternion Rot; public string Clip; public float NTime; public int PropBits;
            public bool Placed;
        }
        static readonly Dictionary<int, Npc> byId = new Dictionary<int, Npc>();
        static float nextScan = -1, nextSend;
        static int sent, recv;

        static readonly string[] SkipRoots = { "PLAYER", "TRAFFIC", "HUMANS", "RACES", "NPC_CARS", "GUI" };

        public static void OnLevelLoaded()
        {
            byId.Clear();
            nextScan = PlayerSync.InGame ? Time.realtimeSinceStartup + 17f : -1;
        }

        // Identifiant stable : empreinte du chemin de l'objet Char (meme scene -> meme nombre partout).
        static int Hash(string s)
        {
            unchecked { uint h = 2166136261; foreach (char c in s) { h ^= c; h *= 16777619; } return (int)h; }
        }

        static void Scan()
        {
            int before = byId.Count;
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(SkinnedMeshRenderer)))
            {
                var smr = (SkinnedMeshRenderer)o;
                if (smr.hideFlags != HideFlags.None || smr.name != "bodymesh") continue;
                Transform ch = smr.transform.parent;
                if (ch == null || ch.name != "Char") continue;
                string root = ch.root.name;
                if (root.StartsWith("MWCoop") || System.Array.IndexOf(SkipRoots, root) >= 0 || !ch.root.gameObject.activeInHierarchy) continue;
                string path = Recon.Path(ch);
                int id = Hash(path);
                if (byId.ContainsKey(id)) continue;
                // Animation principale : celle du squelette, sinon la plus haute dessous (AnimationRoot...).
                Animation anim = null;
                foreach (Animation a in ch.GetComponentsInChildren<Animation>(true)) if (anim == null || a.transform.name == "skeleton") anim = a;
                var props = new List<GameObject>();
                foreach (MeshRenderer mr in ch.GetComponentsInChildren<MeshRenderer>(true)) if (props.Count < 16) props.Add(mr.gameObject);
                byId[id] = new Npc { Id = id, Char = ch, Anim = anim, Props = props.ToArray(), RestPos = ch.localPosition, RestRot = ch.localRotation };
            }
            if (byId.Count != before) Log.Info("PNJ : " + byId.Count + " suivis");
        }

        public static void Update()
        {
            if (!Session.Active || nextScan < 0 || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextScan) { nextScan = now + 45f; Scan(); }
            if (!Session.IsHost || Session.RemoteCount == 0 || now < nextSend) return;
            nextSend = now + 0.2f;
            // Invites en partie : leurs pieds.
            var guests = new List<Vector3>();
            foreach (Avatar a in PlayerSync.Avatars) if (a.Player != null && a.Player.Level == 1) guests.Add(a.Player.State.Feet);
            if (guests.Count == 0) return;
            NetWriter w = null;
            foreach (Npc n in byId.Values)
            {
                if (n.Char == null || !n.Char.gameObject.activeInHierarchy) continue;
                Vector3 p = n.Char.position;
                bool near = false;
                foreach (Vector3 g in guests) if ((g - p).sqrMagnitude < 80f * 80f) { near = true; break; }
                if (!near) continue;
                string clip = ""; float nt = 0f;
                if (n.Anim != null)
                {
                    float best = -1f;
                    foreach (AnimationState st in n.Anim)
                        if (n.Anim.IsPlaying(st.name) && st.weight > best) { best = st.weight; clip = st.name; nt = st.normalizedTime; }
                }
                int bits = 0;
                for (int i = 0; i < n.Props.Length; i++) if (n.Props[i] != null && n.Props[i].activeSelf) bits |= 1 << i;
                int len = 4 + 12 + 16 + 2 + clip.Length + 4 + 2;
                if (w != null && w.Length + len > 1000) { Session.SendAll(w, false); w = null; }
                if (w == null) w = new NetWriter(Msg.Npc);
                w.I32(n.Id).Vec(p).Quat(n.Char.rotation).Str(clip).F32(nt).U16(bits);
                sent++;
            }
            if (w != null) Session.SendAll(w, false);
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            if (Session.IsHost) return;
            float now = Time.realtimeSinceStartup;
            while (r.More)
            {
                int id = r.I32();
                Vector3 p = r.Vec(); Quaternion q = r.Quat(); string clip = r.Str(); float nt = r.F32(); int bits = r.U16();
                Npc n;
                if (!byId.TryGetValue(id, out n) || n.Char == null) continue;
                n.Held = true; n.HeldAt = now; n.Pos = p; n.Rot = q; n.Clip = clip; n.NTime = nt; n.PropBits = bits;
                recv++;
            }
        }

        // Apres l'animation du jeu : corps visible, clip et objets de l'hote.
        public static void LateUpdate()
        {
            if (!Session.Active || Session.IsHost || byId.Count == 0) return;
            float now = Time.realtimeSinceStartup, k = 1f - Mathf.Exp(-12f * Time.deltaTime);
            foreach (Npc n in byId.Values)
            {
                if (!n.Held || n.Char == null) continue;
                if (now - n.HeldAt > 1f || !n.Char.gameObject.activeInHierarchy)
                {
                    n.Held = false; n.Placed = false;
                    n.Char.localPosition = n.RestPos; n.Char.localRotation = n.RestRot;   // rendu a sa propre logique
                    continue;
                }
                if (n.Anim != null && n.Clip.Length > 0)
                {
                    AnimationState st = n.Anim[n.Clip];
                    if (st != null)
                    {
                        if (!n.Anim.IsPlaying(n.Clip)) n.Anim.Play(n.Clip);
                        float t = n.NTime + (now - n.HeldAt) / Mathf.Max(st.length, 0.01f) * st.speed;
                        st.normalizedTime = st.wrapMode == WrapMode.Loop ? t % 1f : Mathf.Min(t, 1f);
                        n.Anim.Sample();
                    }
                }
                if (!n.Placed || (n.Char.position - n.Pos).sqrMagnitude > 25f) { n.Char.position = n.Pos; n.Char.rotation = n.Rot; n.Placed = true; }
                else { n.Char.position = Vector3.Lerp(n.Char.position, n.Pos, k); n.Char.rotation = Quaternion.Slerp(n.Char.rotation, n.Rot, k); }
                for (int i = 0; i < n.Props.Length; i++)
                {
                    if (n.Props[i] == null) continue;
                    bool on = (n.PropBits & (1 << i)) != 0;
                    if (n.Props[i].activeSelf != on) n.Props[i].SetActive(on);
                }
            }
        }

        public static string State(string part)
        {
            foreach (Npc n in byId.Values)
                if (n.Char != null && Recon.Path(n.Char).Contains(part))
                {
                    string clip = "";
                    if (n.Anim != null) foreach (AnimationState st in n.Anim) if (n.Anim.IsPlaying(st.name)) clip += st.name + "@" + st.normalizedTime.ToString("F2") + " ";
                    int active = 0, held = 0;
                    foreach (Npc x in byId.Values) { if (x.Char != null && x.Char.gameObject.activeInHierarchy) active++; if (x.Held) held++; }
                    return Recon.Path(n.Char) + (n.Char.gameObject.activeInHierarchy ? " actif" : " inactif") + " en " + n.Char.position.ToString("F2") + " " + clip + (n.Held ? "(hote)" : "(local)")
                           + ", envoyes " + sent + ", recus " + recv + ", actifs " + active + ", suivis " + held;
                }
            return "?";
        }
    }
}
