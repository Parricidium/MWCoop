using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // PNJ du monde (vendeurs, caissieres, clients, Teimo, Fleetari, boulots...), vus pareil par tous.
    // L'hote envoie, pour les PNJ actifs chez lui a moins de 80 m d'un invite (10 fois par seconde a moins de
    // 40 m, 5 au-dela), son heure, la pose du corps visible (objet Char), TOUTES ses couches d'animation
    // (squelette, AnimationRoot, spine_upper... : clip dominant, second clip d'un fondu, instant, poids ; une
    // couche qui ne joue plus garde la pose de fin de son dernier clip, comme le telephone a l'oreille) et les
    // objets tenus ou poses (telephone, assiette, sonnerie...). Ce dernier clip est suivi pour tous ses PNJ,
    // meme loin des invites ou sans invite en partie : celui qui arrive recoit d'emblee la bonne pose tenue.
    // Chez l'invite, les 8 derniers instantanes de chaque PNJ sont gardes et rejoues 0,25 s apres l'heure de
    // l'hote : pose interpolee entre les deux qui encadrent (extrapolee 0,3 s au plus si le tampon se vide),
    // posee directement, puis les couches appliquees parents d'abord.
    //  - Clients (sous .../Customers/) : tout l'objet du PNJ suit (racine, corps, collisions), sa logique
    //    locale est coupee (marche, telephone, regard) sauf ce qu'un joueur provoque (colere, coup, voiture :
    //    ca va a l'hote) ; et les memes clients sont la (presents ou absents comme chez l'hote).
    //  - Personnel (automate Work : caissiere, serveurs) et autres PNJ : leur logique continue (la caissiere
    //    vient toujours servir l'invite), seul le corps visible suit ; leur telephone est coupe.
    // Sans nouvelles de l'hote depuis 2 s (PNJ loin de lui, eteint chez lui), l'invite revoit les siens (retour
    // en 0,5 s). Hors circulation et passants (Traffic) et courses.
    public static class Npcs
    {
        const int MaxLayers = 4, MaxStates = 2, MaxProps = 32, Ring = 8;
        const float Delay = 0.25f, MaxExtrap = 0.3f, ReleaseAfter = 2f, BlendTime = 0.5f;

        // Une couche : une composante Animation du corps (ses etats releves une fois, empreinte de leur nom).
        class Layer
        {
            public Animation A; public AnimationState[] St; public int[] H; public bool[] Loop; public bool Built;
            public int Last;               // hote : dernier clip dominant (tenu quand plus rien ne joue)
            public int Top = -1, Next = -1;  // hote : les deux etats les plus forts au dernier releve
        }

        // Instantane de l'hote : par couche, jusqu'a 2 etats (clip, instant, poids) aux indices l*MaxStates+k.
        class Snap
        {
            public float T; public bool HasRoot; public Vector3 Pos, RootPos; public Quaternion Rot, RootRot; public uint Props;
            public int Layers;
            public readonly int[] Count = new int[MaxLayers];
            public readonly bool[] Hold = new bool[MaxLayers];
            public readonly int[] Clip = new int[MaxLayers * MaxStates];
            public readonly float[] Nt = new float[MaxLayers * MaxStates], W = new float[MaxLayers * MaxStates];
        }

        class Npc
        {
            public int Id; public string Path; public Transform Char, Root, Customer; public bool Ambient;
            public Layer[] Layers; public GameObject[] Props;
            public Vector3 RestPos; public Quaternion RestRot;
            public bool InRange, Near;                                     // hote : distance aux invites
            public Snap[] Buf; public int Head, Count;                     // invite : anneau trie par heure de l'hote
            public bool Held, Dry; public float LastRecv; public int Extrap;
            public List<PlayMakerFSM> Muted; public List<Rigidbody> Frozen;
            public float BlendUntil; public Vector3 BlendPos; public Quaternion BlendRot;
        }
        static readonly Dictionary<int, Npc> byId = new Dictionary<int, Npc>();
        static readonly List<Npc> all = new List<Npc>();   // les memes, parcourus a chaque image sans allocation
        static readonly List<Vector3> guests = new List<Vector3>();
        static readonly Snap scratch = new Snap();
        static float nextScan = -1, nextSend, lastLate;
        static int sent, recv, tick, held, released, switched;
        static bool anyHeld, anyBlend;
        static Transform me;

        static readonly string[] SkipRoots = { "PLAYER", "TRAFFIC", "HUMANS", "RACES", "NPC_CARS", "GUI" };

        public static void OnLevelLoaded()
        {
            byId.Clear(); all.Clear();
            nextScan = PlayerSync.InGame ? Time.realtimeSinceStartup + 17f : -1;
            offSet = false; offMin = offPrev = float.MaxValue; offWindow = 0f;
            anyHeld = anyBlend = false; me = null;
            watch = null; testStep = 0; testNext = lastShot = 0f; fluHas = false;
        }

        // Identifiant stable : empreinte du chemin de l'objet Char (meme scene -> meme nombre partout).
        static int Hash(string s)
        {
            unchecked { uint h = 2166136261; foreach (char c in s) { h ^= c; h *= 16777619; } return (int)h; }
        }

        static void Scan()
        {
            int before = byId.Count, clients = 0;
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
                var n = new Npc { Id = id, Path = path, Char = ch, RestPos = ch.localPosition, RestRot = ch.localRotation };
                // Objet du PNJ : au-dessus de Pivot (et de PhysicsPivot) ; client : l'enfant de Customers.
                Transform piv = ch.parent;
                n.Root = piv != null && piv.name == "Pivot" ? piv.parent : piv;
                if (n.Root != null && n.Root.name == "PhysicsPivot" && n.Root.parent != null) n.Root = n.Root.parent;
                for (Transform t = ch; t.parent != null; t = t.parent) if (t.parent.name == "Customers") { n.Customer = t; break; }
                n.Ambient = n.Customer != null && n.Root != null && Game.FsmOn(n.Root.gameObject, "Work") == null;
                if (n.Ambient) clients++;
                // Couches : toutes les animations du corps, dans l'ordre de la hierarchie (parents d'abord).
                var layers = new List<Layer>();
                foreach (Animation a in ch.GetComponentsInChildren<Animation>(true)) if (layers.Count < MaxLayers) layers.Add(new Layer { A = a });
                n.Layers = layers.ToArray();
                // Objets tenus : sous le corps ; pour un client, sous tout son objet (plateau, telephone sur la
                // table, sonnerie).
                var props = new List<GameObject>();
                foreach (Transform t in (n.Ambient ? n.Root : ch).GetComponentsInChildren<Transform>(true))
                    if (props.Count < MaxProps && (t.GetComponent<MeshRenderer>() != null || n.Ambient && t.GetComponent<AudioSource>() != null)) props.Add(t.gameObject);
                n.Props = props.ToArray();
                byId[id] = n; all.Add(n);
            }
            if (byId.Count != before) Log.Info("PNJ : " + byId.Count + " suivis (" + clients + " clients de plus)");
        }

        // Etats d'une couche, releves la premiere fois qu'elle est active (pas avant : rien n'est charge).
        static void Build(Layer L)
        {
            if (L.Built || L.A == null || !L.A.gameObject.activeInHierarchy) return;
            L.Built = true;
            var st = new List<AnimationState>();
            foreach (AnimationState s in L.A) st.Add(s);
            L.St = st.ToArray(); L.H = new int[L.St.Length]; L.Loop = new bool[L.St.Length];
            for (int i = 0; i < L.St.Length; i++)
            {
                AnimationState s = L.St[i];
                L.H[i] = Hash(s.name);
                L.Loop[i] = s.wrapMode == WrapMode.Loop || s.clip != null && s.clip.wrapMode == WrapMode.Loop;
            }
        }

        // Logique propre d'un PNJ (pour WorldFsms, qui passe avant le premier releve) : sous un corps (Char) ou un
        // ragdoll (RagDoll), ou sous l'objet qui porte Pivot/Char ou PhysicsPivot/Pivot/Char avec un bodymesh.
        public static bool IsNpcLogic(Transform t)
        {
            for (; t != null; t = t.parent)
            {
                string n = t.name;
                if (n == "Char" || n == "RagDoll") return true;
                if (HasBody(t.Find("Pivot/Char")) || HasBody(t.Find("PhysicsPivot/Pivot/Char"))) return true;
            }
            return false;
        }

        static bool HasBody(Transform ch)
        {
            if (ch == null) return false;
            Transform b = ch.Find("bodymesh");
            return b != null && b.GetComponent<SkinnedMeshRenderer>() != null;
        }

        // ---------------------------------------------------------------- hote
        public static void Update()
        {
            if (!Session.Active || nextScan < 0 || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextScan) { nextScan = now + 45f; Scan(); }
            if (!Session.IsHost || now < nextSend) return;
            nextSend = now + 0.1f;
            // Couches de tous les PNJ, avant tout envoi : un clip qui finit loin des invites compte aussi.
            for (int k = 0; k < all.Count; k++) if (all[k].Char != null) Track(all[k]);
            if (Session.RemoteCount == 0) return;
            bool slow = (++tick & 1) == 0;   // 5 fois par seconde : tous ; entre deux : ceux a moins de 40 m
            // Invites en partie : leurs pieds.
            guests.Clear();
            foreach (Avatar a in PlayerSync.Avatars) if (a.Player != null && a.Player.Level == 1) guests.Add(a.Player.State.Feet);
            if (guests.Count == 0) return;
            NetWriter w = null;
            for (int k = 0; k < all.Count; k++)
            {
                Npc n = all[k];
                if (n.Char == null) continue;
                Vector3 p = n.Char.position;
                float d = float.MaxValue;
                for (int i = 0; i < guests.Count; i++) d = Mathf.Min(d, (guests[i] - p).sqrMagnitude);
                n.InRange = d < (n.InRange ? 90f * 90f : 80f * 80f);
                n.Near = d < (n.Near ? 45f * 45f : 40f * 40f);
                if (!n.InRange || !slow && !n.Near) continue;
                bool present = n.Char.gameObject.activeInHierarchy;
                // Client absent ce jour-la chez l'hote (tire au sort) : dit tel quel, si sa salle est active ici.
                bool absent = !present && slow && n.Customer != null && !n.Customer.gameObject.activeSelf && n.Customer.parent.gameObject.activeInHierarchy;
                if (!present && !absent) continue;
                int len = present ? 38 + (n.Ambient ? 28 : 0) + n.Layers.Length * (1 + MaxStates * 9) : 5;
                if (w != null && w.Length + len > 1000) { Session.SendAll(w, false); w = null; }
                if (w == null) w = new NetWriter(Msg.Npc).F32(now);
                if (absent) { w.I32(n.Id).U8(0); continue; }
                Write(w, n);
                sent++;
            }
            if (w != null) Session.SendAll(w, false);
        }

        // [I32 id][U8 1 present | 2 objet du PNJ joint][Vec Quat corps]([Vec Quat objet du PNJ])[I32 objets tenus]
        // [U8 couches] puis par couche [U8 etats | 0x80 tenu] et par etat [I32 clip][F32 instant][U8 poids].
        static void Write(NetWriter w, Npc n)
        {
            bool root = n.Ambient && n.Root != null;
            w.I32(n.Id).U8(root ? 3 : 1).Vec(n.Char.position).Quat(n.Char.rotation);
            if (root) w.Vec(n.Root.position).Quat(n.Root.rotation);
            uint bits = 0;
            for (int i = 0; i < n.Props.Length; i++) if (n.Props[i] != null && n.Props[i].activeSelf) bits |= 1u << i;
            w.I32((int)bits).U8(n.Layers.Length);
            for (int l = 0; l < n.Layers.Length; l++) WriteLayer(w, n.Layers[l]);
        }

        // Hote, a chaque tour : etats dominants de chaque couche et dernier clip joue. PNJ ou couche eteint : rien
        // de tenu (rallume, il repart de ce que sa logique rejoue).
        static void Track(Npc n)
        {
            bool on = n.Char.gameObject.activeInHierarchy;
            for (int l = 0; l < n.Layers.Length; l++)
            {
                Layer L = n.Layers[l];
                L.Top = L.Next = -1;
                if (!on || L.A == null || !L.A.gameObject.activeInHierarchy) { L.Last = 0; continue; }
                Build(L);
                int a = -1, b = -1;
                for (int i = 0; L.St != null && i < L.St.Length; i++)
                {
                    AnimationState st = L.St[i];
                    if (st == null) { L.Built = false; a = b = -1; break; }
                    if (!st.enabled || st.weight <= 0.001f) continue;
                    if (a < 0 || st.weight > L.St[a].weight) { b = a; a = i; }
                    else if (b < 0 || st.weight > L.St[b].weight) b = i;
                }
                L.Top = a; L.Next = b;
                if (a >= 0) L.Last = L.H[a];
            }
        }

        static void WriteLayer(NetWriter w, Layer L)
        {
            int a = L.Top, b = L.Next;
            if (a < 0)
            {
                // Plus rien ne joue : les os gardent la fin du dernier clip.
                if (L.Last != 0) w.U8(0x81).I32(L.Last).F32(1f).U8(255); else w.U8(0);
                return;
            }
            bool two = b >= 0 && L.St[b].weight > 0.05f;
            w.U8(two ? 2 : 1);
            WriteState(w, L, a);
            if (two) WriteState(w, L, b);
        }

        static void WriteState(NetWriter w, Layer L, int i)
        {
            AnimationState st = L.St[i];
            w.I32(L.H[i]).F32(st.normalizedTime).U8((int)(Mathf.Clamp01(st.weight) * 255f + 0.5f));
        }

        // ---------------------------------------------------------------- invite
        // Decalage d'horloge (heure locale - heure de l'hote) : minimum des 5 a 10 dernieres secondes (le paquet
        // le moins retarde), rejoint doucement (50 ms par seconde) pour que le rejeu ne saute jamais.
        static float offMin = float.MaxValue, offPrev = float.MaxValue, offWindow, offTarget, off;
        static bool offSet;

        static void Clock(float now, float hostT)
        {
            float d = now - hostT;
            if (now >= offWindow) { offPrev = offMin; offMin = float.MaxValue; offWindow = now + 5f; }
            if (d < offMin) offMin = d;
            offTarget = Mathf.Min(offMin, offPrev);
            if (!offSet || Mathf.Abs(offTarget - off) > 0.5f) { off = offTarget; offSet = true; }
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            if (Session.IsHost) return;
            float now = Time.realtimeSinceStartup, hostT = r.F32();
            Clock(now, hostT);
            while (r.More)
            {
                int id = r.I32(), flags = r.U8();
                Npc n;
                if (!byId.TryGetValue(id, out n) || n.Char == null) n = null;
                if ((flags & 1) == 0) { if (n != null) Absent(n); continue; }
                Snap s = n != null ? Slot(n, hostT) : null;
                Read(r, s ?? scratch, flags);
                if (s == null) continue;
                Commit(n);
                recv++;
                n.LastRecv = now;
                Present(n);
                if (!n.Held && n.Char.gameObject.activeInHierarchy) Hold(n);
            }
        }

        // Case libre du tampon (la plus ancienne s'il est plein) ; null si l'instantane est en retard ou double.
        static Snap Slot(Npc n, float t)
        {
            if (n.Buf == null) { n.Buf = new Snap[Ring]; for (int i = 0; i < Ring; i++) n.Buf[i] = new Snap(); }
            if (n.Count > 0)
            {
                float last = n.Buf[(n.Head + n.Count - 1) % Ring].T;
                if (t <= last && last - t < 2f) return null;
                if (t <= last) n.Count = 0;   // l'hote a recommence (nouvelle partie) : on repart de zero
            }
            Snap s = n.Buf[(n.Head + n.Count) % Ring];
            s.T = t;
            return s;
        }

        static void Commit(Npc n) { if (n.Count < Ring) n.Count++; else n.Head = (n.Head + 1) % Ring; }

        static void Read(NetReader r, Snap s, int flags)
        {
            s.Pos = r.Vec(); s.Rot = r.Quat();
            s.HasRoot = (flags & 2) != 0;
            if (s.HasRoot) { s.RootPos = r.Vec(); s.RootRot = r.Quat(); }
            s.Props = (uint)r.I32();
            int nl = r.U8();
            s.Layers = Mathf.Min(nl, MaxLayers);
            for (int l = 0; l < nl; l++)
            {
                int c = r.U8();
                bool hold = (c & 0x80) != 0;
                c &= 0x7f;
                for (int k = 0; k < c; k++)
                {
                    int clip = r.I32(); float nt = r.F32(), wt = r.U8() / 255f;
                    if (l >= MaxLayers || k >= MaxStates) continue;
                    int j = l * MaxStates + k;
                    s.Clip[j] = clip; s.Nt[j] = nt; s.W[j] = wt;
                }
                if (l < MaxLayers) { s.Count[l] = Mathf.Min(c, MaxStates); s.Hold[l] = hold; }
            }
        }

        static bool NearMe(Npc n)
        {
            if (me == null) { GameObject p = GameObject.Find("PLAYER"); if (p == null) return false; me = p.transform; }
            return (me.position - n.Char.position).sqrMagnitude < 100f * 100f;
        }

        // Client present chez l'hote, absent ici (tire au sort autrement) : il vient.
        static void Present(Npc n)
        {
            if (n.Customer == null || n.Customer.gameObject.activeSelf || !NearMe(n)) return;
            n.Customer.gameObject.SetActive(true);
            if (++switched <= 20) Log.Info("PNJ : " + n.Customer.name + " present, comme chez l'hote");
        }

        // Client absent chez l'hote : il s'en va ici aussi.
        static void Absent(Npc n)
        {
            if (n.Customer == null || !n.Customer.gameObject.activeSelf || !NearMe(n)) return;
            if (n.Held) Release(n, Time.realtimeSinceStartup);
            n.BlendUntil = 0f; n.Char.localPosition = n.RestPos; n.Char.localRotation = n.RestRot;
            n.Customer.gameObject.SetActive(false);
            if (++switched <= 20) Log.Info("PNJ : " + n.Customer.name + " absent, comme chez l'hote");
        }

        static string Name(Npc n) { return n.Root != null ? n.Root.name : n.Path; }

        static void Hold(Npc n)
        {
            n.Held = true; n.Dry = false; n.BlendUntil = 0f; anyHeld = true;
            Mute(n);
            if (++held <= 20) Log.Info("PNJ : " + Name(n) + " suit l'hote" + (n.Muted.Count > 0 ? " (" + n.Muted.Count + " automates coupes)" : ""));
        }

        // Client : toute sa logique locale coupee (sauf ce qu'un joueur provoque) et ses corps rigides figes, le
        // temps que l'hote le mene ; les autres : seulement leur telephone (sa sonnerie et ses appels viennent
        // de l'hote).
        static void Mute(Npc n)
        {
            if (n.Muted != null) return;
            n.Muted = new List<PlayMakerFSM>(); n.Frozen = new List<Rigidbody>();
            bool whole = n.Ambient && n.Root != null;
            foreach (PlayMakerFSM f in (whole ? n.Root : n.Char).GetComponentsInChildren<PlayMakerFSM>(true))
            {
                if (!f.enabled) continue;
                if (whole ? WorldFsms.PlayerCaused(f) : !(f.FsmName == "Logic" && f.gameObject.name.StartsWith("Phone"))) continue;
                f.enabled = false; n.Muted.Add(f);
            }
            if (!whole) return;
            foreach (Rigidbody rb in n.Root.GetComponentsInChildren<Rigidbody>(true))
                if (!rb.isKinematic && !InRagdoll(rb.transform, n.Root)) { rb.isKinematic = true; n.Frozen.Add(rb); }
        }

        static bool InRagdoll(Transform t, Transform top)
        {
            for (; t != null && t != top; t = t.parent) if (t.name == "RagDoll") return true;
            return false;
        }

        static void Unmute(Npc n)
        {
            if (n.Muted == null) return;
            foreach (PlayMakerFSM f in n.Muted) if (f != null) f.enabled = true;
            foreach (Rigidbody rb in n.Frozen) if (rb != null) rb.isKinematic = false;
            n.Muted = null; n.Frozen = null;
        }

        // Rendu a sa propre logique : le corps revient a sa place sous son parent en 0,5 s.
        static void Release(Npc n, float now)
        {
            n.Held = false; n.Dry = false; n.Count = 0;
            Unmute(n);
            if (n == watch) fluHas = false;
            n.BlendPos = n.Char.localPosition; n.BlendRot = n.Char.localRotation; n.BlendUntil = now + BlendTime;
            if (++released <= 20) Log.Info("PNJ : " + Name(n) + " rendu a sa logique");
        }

        static void BlendBack(Npc n, float now)
        {
            float k = 1f - (n.BlendUntil - now) / BlendTime;
            if (k >= 1f || !n.Char.gameObject.activeInHierarchy) { n.Char.localPosition = n.RestPos; n.Char.localRotation = n.RestRot; n.BlendUntil = 0f; return; }
            n.Char.localPosition = Vector3.Lerp(n.BlendPos, n.RestPos, k);
            n.Char.localRotation = Quaternion.Slerp(n.BlendRot, n.RestRot, k);
        }

        static float nextErrorLog;

        // Apres l'animation du jeu : pose, couches et objets de l'hote, a l'heure de rejeu.
        public static void LateUpdate()
        {
            if (all.Count == 0 || !anyHeld && !anyBlend) return;
            bool follow = Session.Active && !Session.IsHost;
            float now = Time.realtimeSinceStartup, dt = Mathf.Clamp(now - lastLate, 0f, 0.5f);
            lastLate = now;
            if (offSet) off = Mathf.MoveTowards(off, offTarget, 0.05f * dt);
            float rt = now - off - Delay;
            anyHeld = anyBlend = false;
            for (int k = 0; k < all.Count; k++)
            {
                Npc n = all[k];
                if (n.Char == null) continue;
                try
                {
                    if (n.Held && (!follow || now - n.LastRecv > ReleaseAfter || n.Count == 0 || !n.Char.gameObject.activeInHierarchy)) Release(n, now);
                    if (n.BlendUntil > 0f) { BlendBack(n, now); anyBlend = true; }
                    if (!n.Held) continue;
                    anyHeld = true;
                    Apply(n, rt);
                    if (n == watch) Sample(n, now);
                }
                catch (System.Exception e)
                {
                    if (now >= nextErrorLog) { nextErrorLog = now + 10f; Log.Warn("PNJ : " + n.Path + " : " + e.Message); }
                }
            }
        }

        static Snap At(Npc n, int i) { return n.Buf[(n.Head + i) % Ring]; }

        static void Apply(Npc n, float rt)
        {
            // Les deux instantanes qui encadrent rt (a, b, fraction u) ; apres le dernier : extrapolation.
            Snap a = At(n, 0), b = null, last = At(n, n.Count - 1);
            float u = 0f, ex = 0f;
            Vector3 vel = Vector3.zero, rvel = Vector3.zero;
            if (rt >= last.T)
            {
                a = last;
                ex = Mathf.Min(rt - last.T, MaxExtrap);
                if (rt - last.T > 0.02f) { if (!n.Dry) { n.Dry = true; n.Extrap++; } } else n.Dry = false;
                Snap p = n.Count >= 2 ? At(n, n.Count - 2) : null;
                float span = p != null ? last.T - p.T : 0f;
                if (span > 0.01f && span < 1f && (last.Pos - p.Pos).sqrMagnitude < 25f)
                {
                    vel = (last.Pos - p.Pos) / span;
                    if (last.HasRoot && p.HasRoot) rvel = (last.RootPos - p.RootPos) / span;
                }
            }
            else
            {
                n.Dry = false;
                for (int i = 0; i < n.Count - 1; i++)
                {
                    Snap s1 = At(n, i + 1);
                    if (rt >= s1.T) continue;
                    Snap s0 = At(n, i);
                    if (rt >= s0.T) { a = s0; b = s1; u = (rt - s0.T) / Mathf.Max(s1.T - s0.T, 0.001f); }
                    break;
                }
                // Grand saut chez l'hote (teleporte) : pas de glissade a travers les murs.
                if (b != null && (b.Pos - a.Pos).sqrMagnitude > 25f) { if (u >= 0.5f) a = b; b = null; u = 0f; }
            }
            Vector3 pos; Quaternion rot;
            if (b != null) { pos = Vector3.Lerp(a.Pos, b.Pos, u); rot = Quaternion.Slerp(a.Rot, b.Rot, u); }
            else { pos = a.Pos + vel * ex; rot = a.Rot; }
            if (n.Ambient && a.HasRoot && n.Root != null)
            {
                if (b != null && b.HasRoot) { n.Root.position = Vector3.Lerp(a.RootPos, b.RootPos, u); n.Root.rotation = Quaternion.Slerp(a.RootRot, b.RootRot, u); }
                else { n.Root.position = a.RootPos + rvel * ex; n.Root.rotation = a.RootRot; }
            }
            n.Char.position = pos; n.Char.rotation = rot;
            // Couches : temps de l'hote a l'instant rejoue (extrapole au plus de 0,3 s).
            float at = b != null ? rt : a.T + ex;
            for (int l = 0; l < n.Layers.Length; l++) ApplyLayer(n.Layers[l], l, a, b, u, at);
            uint bits = (b != null && u >= 0.5f ? b : a).Props;
            for (int i = 0; i < n.Props.Length; i++)
            {
                if (n.Props[i] == null) continue;
                bool on = (bits & (1u << i)) != 0;
                if (n.Props[i].activeSelf != on) n.Props[i].SetActive(on);
            }
        }

        // Poids (non normalise) et instant de l'etat i de la couche l a l'heure 'at' : fondu lineaire de a vers b.
        static float Weight(Layer L, int i, int l, Snap a, Snap b, float u, float at, out float nt)
        {
            nt = 0f;
            float w = 0f, rate = L.St[i].speed / Mathf.Max(L.St[i].length, 0.01f);
            int h = L.H[i];
            bool found = false;
            if (l < a.Layers)
                for (int k = 0; k < a.Count[l]; k++)
                {
                    int j = l * MaxStates + k;
                    if (a.Clip[j] != h) continue;
                    w += a.W[j] * (1f - u);
                    nt = a.Hold[l] ? a.Nt[j] : a.Nt[j] + (at - a.T) * rate;
                    found = true;
                    break;
                }
            if (b != null && l < b.Layers)
                for (int k = 0; k < b.Count[l]; k++)
                {
                    int j = l * MaxStates + k;
                    if (b.Clip[j] != h) continue;
                    w += b.W[j] * u;
                    if (!found) nt = b.Hold[l] ? b.Nt[j] : b.Nt[j] - (b.T - at) * rate;
                    break;
                }
            return w;
        }

        // Etats de l'hote actives avec leur poids et leur instant, les autres eteints, puis la couche echantillonnee.
        static void ApplyLayer(Layer L, int l, Snap a, Snap b, float u, float at)
        {
            Build(L);
            if (L.St == null || L.A == null) return;
            float sum = 0f, nt;
            for (int i = 0; i < L.St.Length; i++)
            {
                if (L.St[i] == null) { L.Built = false; return; }
                sum += Weight(L, i, l, a, b, u, at, out nt);
            }
            for (int i = 0; i < L.St.Length; i++)
            {
                AnimationState st = L.St[i];
                float w = Weight(L, i, l, a, b, u, at, out nt);
                w = sum > 0.001f ? w / sum : 0f;
                if (w <= 0.001f) { if (st.enabled) st.enabled = false; continue; }
                st.enabled = true;
                st.weight = w;
                st.normalizedTime = L.Loop[i] ? nt - Mathf.Floor(nt) : st.wrapMode == WrapMode.PingPong ? nt : Mathf.Clamp01(nt);
            }
            if (sum > 0.001f) L.A.Sample();
        }

        // ---------------------------------------------------------------- etat et essais
        static Npc Find(string part)
        {
            foreach (Npc n in all) if (n.Char != null && n.Path.Contains(part)) return n;
            return null;
        }

        static string ClipName(Layer L, int h)
        {
            for (int i = 0; L.H != null && i < L.H.Length; i++) if (L.H[i] == h && L.St[i] != null) return L.St[i].name;
            return h.ToString("x8");
        }

        // Chaque couche : nom:clip@instant(poids), instant ramene a [0,1[ pour un clip en boucle.
        public static string State(string part)
        {
            Npc n = Find(part);
            if (n == null) return "?";
            var sb = new System.Text.StringBuilder(n.Path);
            sb.Append(n.Char.gameObject.activeInHierarchy ? " actif" : " inactif").Append(" en ").Append(n.Char.position.ToString("F2"));
            foreach (Layer L in n.Layers)
            {
                sb.Append(' ').Append(L.A != null ? L.A.name : "?").Append(':');
                int shown = 0;
                for (int i = 0; L.St != null && i < L.St.Length; i++)
                {
                    AnimationState st = L.St[i];
                    if (st == null || !st.enabled || st.weight <= 0.001f) continue;
                    float t = L.Loop[i] ? st.normalizedTime - Mathf.Floor(st.normalizedTime) : st.normalizedTime;
                    sb.Append(shown++ > 0 ? "+" : "").Append(st.name).Append('@').Append(t.ToString("F2"));
                    if (st.weight < 0.995f) sb.Append('(').Append(st.weight.ToString("F2")).Append(')');
                }
                if (shown == 0) sb.Append(L.Last != 0 ? "tenu " + ClipName(L, L.Last) : "-");
            }
            sb.Append(n.Held ? " (hote, tampon " + n.Count + ", extrapolations " + n.Extrap + ")" : " (local)");
            if (n.Customer != null) sb.Append(", client ").Append(n.Customer.gameObject.activeSelf ? "present" : "absent");
            if (n.Muted != null) sb.Append(", ").Append(n.Muted.Count).Append(" automates coupes");
            uint bits = 0;
            for (int i = 0; i < n.Props.Length; i++) if (n.Props[i] != null && n.Props[i].activeSelf) bits |= 1u << i;
            sb.Append(", objets ").Append(bits.ToString("x"));
            int active = 0, follow = 0;
            foreach (Npc x in all) { if (x.Char != null && x.Char.gameObject.activeInHierarchy) active++; if (x.Held) follow++; }
            sb.Append(", envoyes ").Append(sent).Append(", recus ").Append(recv).Append(", actifs ").Append(active).Append(", suivis ").Append(follow);
            return sb.ToString();
        }

        // Essais ([Test] Autotest=..., SuivrePNJ=partie du chemin du PNJ, TeppoSarkain par defaut).
        //  pnjtel : l'hote rend le client present (30 s) puis fait sonner son telephone ([Test] PnjTelephone,
        //    PnjEtat : PhoneSarkain, Ringing) a 40 s ; les deux cotes notent chaque couche toutes les 2 s (et
        //    avec CapturePeriode, regardent le PNJ et le capturent).
        //  pnjfluide : l'invite note toutes les 5 s la fluidite du PNJ suivi (vitesse moyenne, a-coups = ecart
        //    type / moyenne de la vitesse image par image, plus grand saut d'une image, tampon, extrapolations).
        static int testStep;
        static float testNext, lastShot;
        static Npc watch;
        static Vector3 fluLast;
        static bool fluHas;
        static int fluN;
        static double fluSum, fluSum2;
        static float fluMax, fluLastT;

        public static void Test(string mode, float t)
        {
            if (mode != "pnjtel" && mode != "pnjfluide") return;
            string part = Config.Get("Test", "SuivrePNJ", "TeppoSarkain");
            float now = Time.realtimeSinceStartup;
            if (mode == "pnjtel")
            {
                if (Session.IsHost && t > 30f && testStep == 0) { testStep = 1; Log.Info("autotest : " + TestPresent(part)); }
                if (Session.IsHost && t > 40f && testStep == 1) { testStep = 2; Log.Info("autotest : " + TestRing(part)); }
                if (t > 20f && now >= testNext) { testNext = now + 2f; Log.Info("autotest : pnjtel " + State(part)); }
                // [Test] CapturePeriode=N : la camera regarde le PNJ, capture toutes les N s (dumps\pnjtel-t<s>.png).
                int per = Config.GetInt("Test", "CapturePeriode", 0);
                Npc n = per > 0 && t > 20f ? Find(part) : null;
                if (n == null) return;
                LookAt(n.Char.position + Vector3.up * 0.6f);
                if (t - lastShot < per) return;
                lastShot = t;
                string dir = System.IO.Path.Combine(Log.DataDir, "dumps");
                System.IO.Directory.CreateDirectory(dir);
                string png = System.IO.Path.Combine(dir, "pnjtel-t" + ((int)t).ToString("000") + ".png");
                Application.CaptureScreenshot(png);
                Log.Info("autotest : capture " + png);
                return;
            }
            if (Session.IsHost) return;
            if (watch == null || watch.Char == null) { watch = Find(part); fluHas = false; }
            if (t > 20f && now >= testNext)
            {
                testNext = now + 5f;
                Log.Info("autotest : pnjfluide " + Fluid());
                fluN = 0; fluSum = fluSum2 = 0; fluMax = 0f;
            }
        }

        // La camera du joueur vise 'target' (souris du jeu coupee pendant l'essai, curseur jamais touche).
        static void LookAt(Vector3 target)
        {
            GameObject p = GameObject.Find("PLAYER");
            Transform cam = p != null ? p.transform.Find("Pivot/AnimPivot/Camera/FPSCamera") : null;
            if (cam == null) return;
            foreach (Behaviour b in p.GetComponents<Behaviour>()) if (b.GetType().Name.Contains("MouseLook")) b.enabled = false;
            foreach (Behaviour b in cam.GetComponents<Behaviour>()) if (b.GetType().Name.Contains("MouseLook")) b.enabled = false;
            Vector3 to = target - cam.position;
            p.transform.rotation = Quaternion.Euler(0, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, 0);
            cam.localRotation = Quaternion.Euler(-Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg, 0, 0);
        }

        static string TestPresent(string part)
        {
            Npc n = Find(part);
            if (n == null) return "pnjtel : aucun PNJ " + part;
            if (n.Customer == null || n.Customer.gameObject.activeSelf) return "pnjtel : " + n.Path + " deja la";
            n.Customer.gameObject.SetActive(true);
            return "pnjtel : " + n.Customer.name + " rendu present";
        }

        static string TestRing(string part)
        {
            Npc n = Find(part);
            if (n == null) return "pnjtel : aucun PNJ " + part;
            string phone = Config.Get("Test", "PnjTelephone", "PhoneSarkain"), state = Config.Get("Test", "PnjEtat", "Ringing");
            foreach (PlayMakerFSM f in n.Char.GetComponentsInChildren<PlayMakerFSM>(true))
                if (f.gameObject.name == phone && f.FsmName == "Logic")
                {
                    string before = f.ActiveStateName;
                    Game.SetState(f, state);
                    return "pnjtel : " + phone + "::Logic " + before + " => " + f.ActiveStateName;
                }
            return "pnjtel : pas de " + phone + "::Logic sous " + n.Path;
        }

        // Invite, apres la pose : deplacement du corps suivi d'une image a l'autre.
        static void Sample(Npc n, float now)
        {
            Vector3 p = n.Char.position;
            float dt = now - fluLastT;
            fluLastT = now;
            if (fluHas && dt > 0.0005f && dt < 0.5f)
            {
                float d = (p - fluLast).magnitude, v = d / dt;
                fluN++; fluSum += v; fluSum2 += v * v;
                if (d > fluMax) fluMax = d;
            }
            fluLast = p; fluHas = true;
        }

        static string Fluid()
        {
            if (watch == null) return "aucun PNJ suivi";
            double mean = fluN > 0 ? fluSum / fluN : 0, sd = fluN > 0 ? System.Math.Sqrt(System.Math.Max(0, fluSum2 / fluN - mean * mean)) : 0;
            return watch.Path + (watch.Held ? " (hote)" : " (local)") + " : vitesse moyenne " + mean.ToString("F2") + " m/s, a-coups "
                   + (mean > 0.05 ? (sd / mean).ToString("F2") : "-") + ", plus grand saut " + fluMax.ToString("F3") + " m sur " + fluN
                   + " images, tampon " + watch.Count + ", extrapolations " + watch.Extrap + ", decalage " + off.ToString("F3") + " s";
        }
    }
}
