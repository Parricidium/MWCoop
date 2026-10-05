using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Machines a ecran (automates des pompes a essence : carte, code, montant, pompe, recu...). Leur
    // logique touche a l'argent et a l'ecran du joueur : on ne la rejoue pas. Celui qui s'en sert (a
    // moins de 3,5 m, vient de cliquer ou d'appuyer sur une touche) envoie ce qu'on en VOIT : textes de
    // l'ecran, pieces visibles ou cachees (carte, billet, boutons, chiffres), position des pieces qui
    // bougent. Chez les autres ces valeurs l'emportent, apres la logique du jeu, tant qu'il s'en sert ;
    // 3 s apres son dernier envoi la machine revient a son propre etat. Ni paiement en double, ni
    // carte avalee chez l'autre.
    // Machines a jeu (video poker 'Rami-Pokeri' et machine a sous de la station, machine a sous du bar,
    // SlotMachinePub, meme modele) : un seul joueur a la fois.
    //  - Verrou arbitre par l'hote (Msg.MachineLock) : demande au premier clic sur un bouton, accorde s'il
    //    est libre ; bail de 20 s renouvele a chaque clic ; rendu a plus de 3,5 m, a l'expiration du bail
    //    ou au depart du joueur. Celui qui clique joue tout de suite (sa logique, son argent, son hasard) ;
    //    refuse (deux clics croises), il devient spectateur.
    //  - Spectateurs : tous les automates de la machine sont arretes ici (ils reprendront ou ils en etaient,
    //    sans repartir du debut), les boutons ne se cliquent plus (colliders coupes) et un message dit qui
    //    l'occupe. Le joueur envoie a 10 par seconde ce qu'on en voit : pieces allumees ou cachees, textes,
    //    materiaux et textures (cartes, voyants, ecran ; retrouves ici par leur nom), camera de l'ecran,
    //    rouleaux (angle final ; tant qu'ils tournent chez lui, ils tournent ici a vitesse fixe puis
    //    s'arretent sur cet angle). A la fin, chaque piece reprend son etat d'avant chez le spectateur.
    //    Les credits et l'argent restent ceux de chacun : ce que la machine paie (encaissement, gain pris,
    //    mise reprise) n'est pas un revenu a partager, Wallet le demande a KeepsMoney.
    //  - Ordinateur de la maison : chacun joue chez lui ; les autres voient "X joue a Massacre".
    public static class Machines
    {
        class Part
        {
            public string Key; public Transform T; public TextMesh Text; public bool Ext;   // Ext : hors de la machine (ecran du terminal)
            public bool Active; public Vector3 Pos; public Quaternion Rot; public string Txt;          // dernier envoye
            public bool Held; public bool HActive; public bool HPose; public Vector3 HPos; public Quaternion HRot; public string HTxt; public float HeldAt;
            // machines a jeu : rendu (pas pour les textes), camera, rouleau
            public Renderer R; public Camera Cam; public bool Reel;
            public Material Mat; public string MatName = ""; public bool HasTex; public Texture Tex; public string TexName = "";   // dernier envoye
            public bool CamOn, Moved, Spin;
            public string HMat, HTex; public bool HCamSet, HCam, HSpin;                                                           // recu
            public string ShownTxt, ShownMat, ShownTex; public bool Block, Spinning; public float SpinUntil;                     // applique ici
            public bool SActive; public Vector3 SPos; public Quaternion SRot; public string STxt; public Material SMat; public bool SCam;   // ici avant
        }
        class Lock { public int Owner = -1; public float Until, Seen; }
        class Machine
        {
            public string Key, Name; public Transform Root; public List<Part> Parts = new List<Part>(); public Dictionary<string, Part> ByKey = new Dictionary<string, Part>(); public float OwnerUntil, NextFull;
            // machines a jeu
            public bool Lockable; public Transform Center; public Lock L;
            public List<PlayMakerFSM> Fsms, Buttons; public List<Collider> Colliders;
            public readonly List<PlayMakerFSM> Frozen = new List<PlayMakerFSM>(); public readonly List<bool> Restart = new List<bool>();
            public readonly List<Collider> Cut = new List<Collider>();
            public bool Spectating, Hover, Toasted, Watched; public int HeldFrom = -1; public float NextRenew;
        }

        const float Lease = 20f, Reach = 3.5f, SpinSpeed = 720f, KeepMoney = 5f;
        const int L_FREE = 0, L_ASK = 1, L_HELD = 2, L_REFUSED = 3, L_GAME = 4;   // Msg.MachineLock

        static readonly Dictionary<string, Machine> machines = new Dictionary<string, Machine>();
        static readonly List<Machine> lockables = new List<Machine>();
        static readonly Dictionary<string, Lock> locks = new Dictionary<string, Lock>();
        static float nextScan = -1, nextSend, lastInput = -100, nextLockSend, nextLockTick, nextRefresh;
        static Transform player;

        static Machines()
        {
            Session.PlayerLeft += OnPlayerLeft;
        }

        static bool IsMachine(Transform t) { string n = t.name; return n.StartsWith("FuelPumps_") || IsLockable(n); }
        static bool IsLockable(string n) { return n == "VideoPoker" || n.StartsWith("SlotMachine"); }   // SlotMachine (station), SlotMachinePub (bar)

        public static void OnLevelLoaded()
        {
            machines.Clear(); lockables.Clear(); locks.Clear(); player = null;
            matByName = null; texByName = null; system = null; playing = ""; keepMoneyUntil = 0;
            testStep = 0; testNextLog = 0; testSeq = null; testRefus = null; testMoneyBase = testMoneyDone = false;
            nextScan = PlayerSync.InGame ? Time.realtimeSinceStartup + 15f : -1;
            nextComputer = Time.realtimeSinceStartup + 20f;
        }

        static void Scan()
        {
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(Transform)))
            {
                var t = (Transform)o;
                if (t.gameObject.hideFlags != HideFlags.None || !IsMachine(t) || !t.root.gameObject.activeInHierarchy) continue;
                string key = Recon.Path(t);
                if (machines.ContainsKey(key) || t.GetComponentsInChildren<PlayMakerFSM>(true).Length == 0) continue;
                var m = new Machine { Key = key, Root = t, Name = t.name, Lockable = IsLockable(t.name) };
                // Machine a jeu : seulement ce qui se voit ou s'entend (les 104 cartes du paquet, simple logique
                // que SetParent promene, n'en sont pas).
                HashSet<Transform> visible = m.Lockable ? Visible(t) : null;
                var seen = new Dictionary<string, int>();
                foreach (Transform c in t.GetComponentsInChildren<Transform>(true))
                {
                    if (c == t) continue;
                    string rel = Recon.Path(c).Substring(key.Length);
                    int k; seen.TryGetValue(rel, out k); seen[rel] = k + 1;
                    if (visible != null && !visible.Contains(c)) continue;
                    var p = new Part { Key = rel + "#" + k, T = c, Text = c.GetComponent<TextMesh>(), Active = c.gameObject.activeSelf, Pos = c.localPosition, Rot = c.localRotation };
                    if (p.Text != null) p.Txt = p.Text.text;
                    if (m.Lockable)
                    {
                        if (p.Text == null) p.R = c.GetComponent<Renderer>();
                        p.Cam = c.GetComponent<Camera>();
                        p.CamOn = p.Cam != null && p.Cam.enabled;
                        p.Reel = p.R != null && c.parent != null && c.parent.name == "Rolls";
                    }
                    m.Parts.Add(p);
                    m.ByKey[p.Key] = p;
                }
                AddScreen(m);
                machines[key] = m;
                if (m.Lockable) SetupLock(m);
            }
            if (machines.Count > 0) Log.Info("machines a ecran : " + machines.Count + " suivies");
        }

        // Ecran du terminal de la pompe (code PIN, montant, messages) : il n'est pas sous la pompe mais sous
        // PERAPORTTI/ActiveFunctions/ATMs/FuelATM, que l'automate 'Logic' de la pompe designe (ScreenPIN). Sans lui,
        // les autres voyaient le premier '*' du code puis plus rien.
        static void AddScreen(Machine m)
        {
            foreach (PlayMakerFSM f in m.Root.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                FsmGameObject sp = f.FsmVariables.FindFsmGameObject("ScreenPIN");
                if (sp == null || sp.Value == null) continue;
                Transform scr = sp.Value.transform.parent;
                if (scr == null || scr.IsChildOf(m.Root)) return;
                Transform top = scr.parent ?? scr;
                string tk = Recon.Path(top);
                var seen = new Dictionary<string, int>();
                int n = 0;
                foreach (Transform c in top.GetComponentsInChildren<Transform>(true))
                {
                    if (c == top) continue;
                    string rel = "@" + Recon.Path(c).Substring(tk.Length);
                    int k; seen.TryGetValue(rel, out k); seen[rel] = k + 1;
                    var p = new Part { Key = rel + "#" + k, T = c, Ext = true, Text = c.GetComponent<TextMesh>(), Active = c.gameObject.activeSelf, Pos = c.localPosition, Rot = c.localRotation };
                    if (p.Text != null) p.Txt = p.Text.text;
                    m.Parts.Add(p);
                    m.ByKey[p.Key] = p;
                    n++;
                }
                Log.Info("machines a ecran : " + m.Name + " + ecran " + tk + " (" + n + " pieces)");
                return;
            }
        }

        // Pieces qui portent un rendu, un texte, une camera ou un son, et leurs parents jusqu'a la machine.
        static HashSet<Transform> Visible(Transform root)
        {
            var set = new HashSet<Transform>();
            foreach (Component c in root.GetComponentsInChildren<Component>(true))
            {
                if (!(c is Renderer || c is TextMesh || c is Camera || c is AudioSource)) continue;
                for (Transform x = c.transform; x != null && x != root; x = x.parent) if (!set.Add(x)) break;
            }
            return set;
        }

        // Boutons (automates 'Use' et leur collider) ; ses automates ne sont menes que par ce module (le
        // monde ne les rejoue pas : chez l'autre ils tireraient d'autres cartes et crediteraient sa machine).
        static void SetupLock(Machine m)
        {
            m.Fsms = new List<PlayMakerFSM>(m.Root.GetComponentsInChildren<PlayMakerFSM>(true));
            m.Buttons = new List<PlayMakerFSM>();
            m.Colliders = new List<Collider>();
            int mine = 0;
            foreach (PlayMakerFSM f in m.Fsms)
            {
                if (Replay.Claim(f, "machines")) mine++;
                if (f.FsmName != "Use") continue;
                m.Buttons.Add(f);
                Collider c = f.GetComponent<Collider>();
                if (c != null) m.Colliders.Add(c);
                if (m.Center == null) m.Center = f.transform;
            }
            m.L = GetLock(m.Key);
            lockables.Add(m);
            Log.Info("machine a jeu " + m.Name + " : " + m.Parts.Count + " pieces visibles, " + m.Buttons.Count + " boutons, "
                     + mine + "/" + m.Fsms.Count + " automates a ce module");
        }

        public static void Update()
        {
            if (!Session.Active || nextScan < 0 || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextScan) { nextScan = machines.Count == 0 ? now + 30f : float.MaxValue; Scan(); }
            Computer(now);
            if (machines.Count == 0) return;
            if (player == null) { GameObject g = GameObject.Find("PLAYER"); if (g == null) return; player = g.transform; }
            if (Input.anyKeyDown || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) lastInput = now;
            UpdateLocks(now);
            if (now < nextSend || Session.RemoteCount == 0) return;
            nextSend = now + 0.2f;
            foreach (Machine m in machines.Values)
            {
                if (m.Root == null || m.Lockable) continue;
                if (now - lastInput < 1f && Near(m)) m.OwnerUntil = now + 15f;   // il s'en sert
                if (now > m.OwnerUntil) continue;
                bool full = now >= m.NextFull;
                if (full) m.NextFull = now + 2f;
                NetWriter w = null;
                foreach (Part p in m.Parts)
                {
                    if (p.T == null || p.Held || (!p.Ext && !p.T.IsChildOf(m.Root))) continue;   // pistolet decroche : il n'est plus a la machine
                    bool act = p.T.gameObject.activeSelf;
                    bool pose = Quaternion.Angle(p.T.localRotation, p.Rot) > 0.5f || (p.T.localPosition - p.Pos).sqrMagnitude > 1e-6f;
                    string txt = p.Text != null ? p.Text.text : null;
                    bool txtCh = p.Text != null && txt != p.Txt;
                    if (!full && act == p.Active && !pose && !txtCh) continue;   // toutes les 2 s : tout
                    p.Active = act; p.Pos = p.T.localPosition; p.Rot = p.T.localRotation; p.Txt = txt;
                    bool withPose = pose || full;
                    int len = 12 + p.Key.Length + (withPose ? 28 : 0) + (txt != null ? txt.Length * 2 + 2 : 0);
                    if (w != null && w.Length + len > 900) { Session.SendAll(w, false); w = null; }
                    if (w == null) w = new NetWriter(Msg.Machine).U8(Session.LocalId).Str(m.Key);
                    w.Str(p.Key).U8((act ? 1 : 0) | (withPose ? 2 : 0) | (txt != null ? 4 : 0));
                    if (withPose) w.Vec(p.Pos).Quat(p.Rot);
                    if (txt != null) w.Str(txt);
                }
                if (w != null) Session.SendAll(w, false);
            }
        }

        static bool Near(Machine m)
        {
            if (m.Center != null) return (player.position - m.Center.position).sqrMagnitude < Reach * Reach;
            Vector3 c = m.Root.position;
            foreach (PlayMakerFSM f in m.Root.GetComponentsInChildren<PlayMakerFSM>()) { c = f.transform.position; break; }
            return (player.position - c).sqrMagnitude < 3.5f * 3.5f;
        }

        // Apres la logique du jeu : ce que voit celui qui s'en sert l'emporte.
        public static void LateUpdate()
        {
            if (!Session.Active || machines.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            foreach (Machine m in machines.Values)
            {
                if (m.Lockable) { if (m.Spectating && m.Root != null && m.Root.gameObject.activeInHierarchy) Apply(m, now); continue; }
                foreach (Part p in m.Parts)
                {
                    if (!p.Held || p.T == null) continue;
                    if (now - p.HeldAt > 3f) { p.Held = false; p.Active = p.T.gameObject.activeSelf; p.Pos = p.T.localPosition; p.Rot = p.T.localRotation; p.Txt = p.Text != null ? p.Text.text : null; continue; }
                    if (p.T.gameObject.activeSelf != p.HActive) p.T.gameObject.SetActive(p.HActive);
                    if (p.HPose) { p.T.localPosition = p.HPos; p.T.localRotation = p.HRot; }
                    if (p.Text != null && p.HTxt != null && p.Text.text != p.HTxt) p.Text.text = p.HTxt;
                }
            }
        }

        // Drapeaux d'une piece : 1 active, 2 pose, 4 texte, 8 materiau, 16 texture, 32 camera (64 allumee),
        // 128 rouleau qui tourne encore.
        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            string key = r.Str();
            NetWriter relay = Session.IsHost ? new NetWriter(Msg.Machine).U8(who).Str(key) : null;
            Machine m;
            machines.TryGetValue(key, out m);
            // Machine a jeu : seulement ce qu'envoie celui qui la tient (un envoi en retard de l'ancien ne compte pas).
            bool take = m == null || !m.Lockable || !m.Spectating || who == m.L.Owner;
            if (take && m != null && m.Lockable && m.HeldFrom != who) { ClearHeld(m); m.HeldFrom = who; }
            float now = Time.realtimeSinceStartup;
            while (r.More)
            {
                string pk = r.Str();
                int fl = r.U8();
                Vector3 pos = Vector3.zero; Quaternion rot = Quaternion.identity; string txt = null, mat = null, tex = null;
                if ((fl & 2) != 0) { pos = r.Vec(); rot = r.Quat(); }
                if ((fl & 4) != 0) txt = r.Str();
                if ((fl & 8) != 0) mat = r.Str();
                if ((fl & 16) != 0) tex = r.Str();
                if (relay != null)
                {
                    relay.Str(pk).U8(fl);
                    if ((fl & 2) != 0) relay.Vec(pos).Quat(rot);
                    if (txt != null) relay.Str(txt);
                    if (mat != null) relay.Str(mat);
                    if (tex != null) relay.Str(tex);
                }
                Part p;
                if (!take || m == null || !m.ByKey.TryGetValue(pk, out p) || p.T == null) continue;
                p.Held = true; p.HeldAt = now; p.HActive = (fl & 1) != 0;
                if ((fl & 2) != 0) { p.HPose = true; p.HPos = pos; p.HRot = rot; }
                if (txt != null) p.HTxt = txt;
                if (!m.Lockable) continue;
                if (mat != null) p.HMat = mat;
                if (tex != null) p.HTex = tex;
                if ((fl & 32) != 0) { p.HCamSet = true; p.HCam = (fl & 64) != 0; }
                if ((fl & 2) != 0) p.HSpin = (fl & 128) != 0;
            }
            if (relay != null) Session.Broadcast(relay, false, who);
            if (m != null) m.OwnerUntil = 0f;   // un autre s'en sert : on n'envoie pas en meme temps
        }

        // ---------------------------------------------------------------- machines a jeu : verrou
        static Lock GetLock(string key)
        {
            Lock l;
            if (!locks.TryGetValue(key, out l)) { l = new Lock(); locks[key] = l; }
            return l;
        }

        static string NameOf(int id)
        {
            PlayerInfo pi;
            return Session.Players.TryGetValue(id, out pi) ? pi.Name : "joueur " + id;
        }

        static string Short(string key) { return key.Substring(key.LastIndexOf('/') + 1); }

        static void SetOwner(string key, int owner, string why)
        {
            Lock l = GetLock(key);
            l.Seen = Time.realtimeSinceStartup;
            if (l.Owner == owner) return;
            if (l.Owner == Session.LocalId) keepMoneyUntil = l.Seen + KeepMoney;   // ce qu'elle paie encore (Take Win attend) reste a lui
            l.Owner = owner;
            Machine m;
            if (owner == Session.LocalId && machines.TryGetValue(key, out m)) m.NextFull = 0f;   // tout de suite tout
            Log.Info("machine " + Short(key) + (owner >= 0 ? " : tenue par " + NameOf(owner) : " : libre") + " (" + why + ")");
        }

        // Chaque image : spectateur ou non, clics sur les boutons, depart du joueur ; envoi de ce qu'il voit.
        static void UpdateLocks(float now)
        {
            if (lockables.Count == 0) return;
            bool click = Input.GetMouseButtonDown(0);
            bool tick = now >= nextLockTick;
            if (tick) nextLockTick = now + 1f;
            if (tick && Session.IsHost) HostTick(now);
            foreach (Machine m in lockables)
            {
                if (m.Root == null) continue;
                Lock l = m.L;
                // Plus de nouvelles de l'hote depuis 10 s (il les renvoie toutes les 3 s) : libre.
                if (tick && !Session.IsHost && l.Owner >= 0 && now - l.Seen > 10f) SetOwner(m.Key, -1, "plus de nouvelles de l'hote");
                bool spect = l.Owner >= 0 && l.Owner != Session.LocalId;
                if (spect != m.Spectating) { if (spect) Freeze(m); else Thaw(m); }
                bool near = Near(m);
                if (spect)
                {
                    if (near && !m.Toasted) Hud.Toast("Machine occupee par " + NameOf(l.Owner));
                    m.Toasted = near;
                    continue;
                }
                m.Toasted = false;
                if (l.Owner == Session.LocalId && !near) { Release(m); continue; }
                if (!near) { m.Hover = false; continue; }
                // Clic : un bouton de cette machine est sous la souris (ou l'etait a l'image d'avant, selon que
                // le jeu a deja traite le clic).
                bool hover = Hovered(m);
                if (click && (hover || m.Hover)) Press(m);
                m.Hover = hover;
            }
            if (now < nextLockSend || Session.RemoteCount == 0) return;
            nextLockSend = now + 0.1f;
            foreach (Machine m in lockables)
            {
                if (m.Root == null || m.L.Owner != Session.LocalId) continue;
                bool watched = Watched(m);
                if (watched && !m.Watched) m.NextFull = 0f;
                m.Watched = watched;
                if (watched) SendLocked(m, now);
            }
        }

        static bool Hovered(Machine m)
        {
            foreach (PlayMakerFSM f in m.Buttons)
            {
                if (f == null || !f.enabled) continue;
                string s = f.ActiveStateName;
                if (!string.IsNullOrEmpty(s) && s != "Wait player") return true;
            }
            return false;
        }

        // Un autre joueur a moins de 60 m : sinon rien a envoyer.
        static bool Watched(Machine m)
        {
            Vector3 c = m.Center != null ? m.Center.position : m.Root.position;
            foreach (PlayerInfo pi in Session.Players.Values)
                if (!pi.Local && pi.Level == 1 && pi.StateTime > 0 && (pi.State.Feet - c).sqrMagnitude < 60f * 60f) return true;
            return false;
        }

        // Argent : ce que paie une machine a jeu (encaissement de la machine a sous, gain pris ou mise reprise
        // au poker) reste a celui qui y joue, comme ses mises restent a sa charge. Wallet le demande avant de
        // partager une hausse du liquide : vrai tant qu'il tient une machine ou que sa souris est sur un de
        // ses boutons, et 5 s apres son dernier clic ou apres l'avoir rendue (Take Win paie apres une attente).
        static float keepMoneyUntil;
        public static bool KeepsMoney
        {
            get
            {
                if (Time.realtimeSinceStartup < keepMoneyUntil) return true;
                foreach (Machine m in lockables) if (m.L.Owner == Session.LocalId || m.Hover) return true;
                return false;
            }
        }

        static void Press(Machine m)
        {
            float now = Time.realtimeSinceStartup;
            keepMoneyUntil = now + KeepMoney;
            if (m.L.Owner == Session.LocalId && now < m.NextRenew) return;   // bail renouvele au plus 1 fois / s
            m.NextRenew = now + 1f;
            Ask(m);
        }

        // Vrai : accorde (hote) ou demande (invite, qui joue sans attendre la reponse).
        static bool Ask(Machine m)
        {
            if (Session.IsHost) return HostAsk(Session.LocalId, m.Key);
            if (m.L.Owner >= 0 && m.L.Owner != Session.LocalId) { Hud.Toast("Machine occupee par " + NameOf(m.L.Owner)); return false; }
            Session.SendToHost(new NetWriter(Msg.MachineLock).U8(L_ASK).U8(Session.LocalId).Str(m.Key), true);
            if (m.L.Owner != Session.LocalId) SetOwner(m.Key, Session.LocalId, "demandee");
            return true;
        }

        static void Release(Machine m)
        {
            if (Session.IsHost) { HostRelease(Session.LocalId, m.Key, "joueur parti de la machine"); return; }
            Session.SendToHost(new NetWriter(Msg.MachineLock).U8(L_FREE).U8(Session.LocalId).Str(m.Key), true);
            SetOwner(m.Key, -1, "joueur parti de la machine");
        }

        static bool HostAsk(int who, string key)
        {
            Lock l = GetLock(key);
            float now = Time.realtimeSinceStartup;
            if (l.Owner >= 0 && l.Owner != who && now < l.Until)
            {
                Log.Info("machine " + Short(key) + " : refusee a " + NameOf(who) + ", tenue par " + NameOf(l.Owner));
                if (who == Session.LocalId) { Hud.Toast("Machine occupee par " + NameOf(l.Owner)); return false; }
                PlayerInfo pi;
                if (Session.Players.TryGetValue(who, out pi) && pi.Peer != null && Session.T != null)
                    Session.T.SendReliable(pi.Peer, new NetWriter(Msg.MachineLock).U8(L_REFUSED).U8(l.Owner).Str(key).ToArray());
                return false;
            }
            l.Until = now + Lease;
            if (l.Owner == who) return true;
            SetOwner(key, who, "prise");
            Session.Broadcast(new NetWriter(Msg.MachineLock).U8(L_HELD).U8(who).Str(key), true);
            return true;
        }

        static void HostRelease(int who, string key, string why)
        {
            if (who < 0 || GetLock(key).Owner != who) return;
            SetOwner(key, -1, why);
            Session.Broadcast(new NetWriter(Msg.MachineLock).U8(L_FREE).U8(who).Str(key), true);
        }

        // Hote, chaque seconde : bail expire, joueur parti loin de la machine ; toutes les 3 s, les verrous
        // tenus sont renvoyes (un invite arrive en cours de partie les apprend ainsi).
        static void HostTick(float now)
        {
            bool refresh = now >= nextRefresh;
            if (refresh) nextRefresh = now + 3f;
            foreach (KeyValuePair<string, Lock> kv in locks)
            {
                Lock l = kv.Value;
                if (l.Owner < 0) continue;
                if (now > l.Until) { HostRelease(l.Owner, kv.Key, "bail expire"); continue; }
                if (l.Owner != Session.LocalId && TooFar(kv.Key, l.Owner)) { HostRelease(l.Owner, kv.Key, NameOf(l.Owner) + " est loin"); continue; }
                if (refresh) Session.Broadcast(new NetWriter(Msg.MachineLock).U8(L_HELD).U8(l.Owner).Str(kv.Key), false);
            }
        }

        static bool TooFar(string key, int owner)
        {
            Machine m; PlayerInfo pi;
            if (!machines.TryGetValue(key, out m) || m.Center == null || !Session.Players.TryGetValue(owner, out pi) || pi.StateTime <= 0) return false;
            return (pi.State.Feet - m.Center.position).sqrMagnitude > (Reach + 1.5f) * (Reach + 1.5f);
        }

        static void OnPlayerLeft(PlayerInfo pi)
        {
            var keys = new List<string>();
            foreach (KeyValuePair<string, Lock> kv in locks)
                if (kv.Value.Owner >= 0 && (kv.Value.Owner == pi.Id || !Session.IsHost && pi.Id == 0)) keys.Add(kv.Key);
            foreach (string k in keys)
            {
                if (Session.IsHost) HostRelease(pi.Id, k, pi.Name + " est parti");
                else SetOwner(k, -1, pi.Name + " est parti");
            }
        }

        // Msg.MachineLock : U8 operation, U8 joueur, Str machine (jeu de l'ordinateur pour L_GAME).
        public static void OnLock(Peer from, NetReader r)
        {
            int op = r.U8(), who = r.U8();
            string key = r.Str();
            if (Session.IsHost)
            {
                who = from.Id;   // un invite ne parle que pour lui-meme
                if (op == L_ASK) HostAsk(who, key);
                else if (op == L_FREE) HostRelease(who, key, NameOf(who) + " l'a lachee");
                else if (op == L_GAME) { Session.Broadcast(new NetWriter(Msg.MachineLock).U8(L_GAME).U8(who).Str(key), true, who); Played(who, key); }
                return;
            }
            if (op == L_HELD) SetOwner(key, who, "hote");
            else if (op == L_FREE) { if (GetLock(key).Owner == who) SetOwner(key, -1, "hote"); }
            else if (op == L_REFUSED) { SetOwner(key, who, "refusee par l'hote"); Hud.Toast("Machine occupee par " + NameOf(who)); }
            else if (op == L_GAME) Played(who, key);
        }

        // ---------------------------------------------------------------- machines a jeu : spectateur
        static void Freeze(Machine m)
        {
            m.Spectating = true; m.Hover = false;
            if (m.HeldFrom != m.L.Owner) ClearHeld(m);
            bool hovered = false;
            m.Frozen.Clear(); m.Restart.Clear(); m.Cut.Clear();
            foreach (PlayMakerFSM f in m.Fsms)
            {
                if (f == null || !f.enabled) continue;
                if (f.FsmName == "Use" && f.ActiveStateName == "Wait button") hovered = true;
                m.Frozen.Add(f); m.Restart.Add(f.Fsm.RestartOnEnable);
                f.Fsm.RestartOnEnable = false;   // a la reprise : la ou il en etait, pas depuis le debut
                f.enabled = false;
            }
            foreach (Collider c in m.Colliders) if (c != null && c.enabled) { c.enabled = false; m.Cut.Add(c); }
            if (hovered) { FsmBool use = FsmVariables.GlobalVariables.FindFsmBool("GUIuse"); if (use != null) use.Value = false; }   // main du curseur
            foreach (Part p in m.Parts)
            {
                if (p.T == null) continue;
                p.SActive = p.T.gameObject.activeSelf; p.SPos = p.T.localPosition; p.SRot = p.T.localRotation;
                p.STxt = p.Text != null ? p.Text.text : null;
                p.SMat = p.R != null ? p.R.sharedMaterial : null;
                p.SCam = p.Cam != null && p.Cam.enabled;
                p.ShownTxt = p.ShownMat = p.ShownTex = null; p.Spinning = false;
            }
            Log.Info("machine " + m.Name + " : spectateur de " + NameOf(m.L.Owner) + " (" + m.Frozen.Count + " automates et " + m.Cut.Count + " boutons arretes ici)");
        }

        static void Thaw(Machine m)
        {
            m.Spectating = false;
            foreach (Part p in m.Parts)
            {
                if (p.T == null) continue;
                if (p.T.gameObject.activeSelf != p.SActive) p.T.gameObject.SetActive(p.SActive);
                p.T.localPosition = p.SPos; p.T.localRotation = p.SRot;
                if (p.Text != null && p.STxt != null && p.Text.text != p.STxt) p.Text.text = p.STxt;
                if (p.R != null && p.SMat != null && p.R.sharedMaterial != p.SMat) p.R.sharedMaterial = p.SMat;
                if (p.Block) { Block().Clear(); p.R.SetPropertyBlock(Block()); p.Block = false; }
                if (p.Cam != null) p.Cam.enabled = p.SCam;
                p.STxt = null; p.SMat = null; p.Spinning = false;
            }
            for (int i = 0; i < m.Frozen.Count; i++)
            {
                PlayMakerFSM f = m.Frozen[i];
                if (f == null) continue;
                f.enabled = true;
                f.Fsm.RestartOnEnable = m.Restart[i];
            }
            foreach (Collider c in m.Cut) if (c != null) c.enabled = true;
            Log.Info("machine " + m.Name + " : rendue (" + m.Frozen.Count + " automates repris)");
            m.Frozen.Clear(); m.Restart.Clear(); m.Cut.Clear();
            ClearHeld(m);
        }

        static void ClearHeld(Machine m)
        {
            foreach (Part p in m.Parts) { p.Held = false; p.HPose = false; p.HTxt = p.HMat = p.HTex = null; p.HCamSet = false; p.HSpin = false; }
            m.HeldFrom = -1;
        }

        // Ce que voit le joueur qui la tient l'emporte (ici ses automates sont arretes : rien ne lutte contre).
        static void Apply(Machine m, float now)
        {
            float dt = Time.deltaTime;
            foreach (Part p in m.Parts)
            {
                if (!p.Held || p.T == null) continue;
                if (p.T.gameObject.activeSelf != p.HActive) p.T.gameObject.SetActive(p.HActive);
                if (p.HPose) { if (p.Reel) Spin(p, dt, now); else { p.T.localPosition = p.HPos; p.T.localRotation = p.HRot; } }
                if (p.Text != null && p.HTxt != null && p.HTxt != p.ShownTxt) { p.Text.text = p.HTxt; p.ShownTxt = p.HTxt; }
                if (p.Cam != null && p.HCamSet && p.Cam.enabled != p.HCam) p.Cam.enabled = p.HCam;
                if (p.R != null) ApplyLook(p);
            }
        }

        // Rouleau : tourne ici tant qu'il tourne chez le joueur, puis jusqu'a son angle final (1,5 s au plus).
        static void Spin(Part p, float dt, float now)
        {
            if (p.HSpin) { p.T.localRotation = p.T.localRotation * Quaternion.Euler(-SpinSpeed * dt, 0f, 0f); p.Spinning = true; p.SpinUntil = now + 1.5f; return; }
            if (!p.Spinning) { p.T.localRotation = p.HRot; return; }
            float step = SpinSpeed * dt;
            if (Quaternion.Angle(p.T.localRotation, p.HRot) <= step || now > p.SpinUntil) { p.T.localRotation = p.HRot; p.Spinning = false; }
            else p.T.localRotation = p.T.localRotation * Quaternion.Euler(-step, 0f, 0f);
        }

        // Materiau par son nom ; texture (SetMaterialTexture du jeu : une copie du materiau chez le joueur) posee
        // par un bloc de proprietes, sans copie ici.
        static void ApplyLook(Part p)
        {
            if (p.HMat != null && p.HMat != p.ShownMat)
            {
                p.ShownMat = p.HMat; p.ShownTex = null;
                Material cur = p.R.sharedMaterial;
                if (p.HMat.Length > 0 && (cur == null || BaseName(cur.name) != p.HMat))
                {
                    Material want = FindMat(p.HMat);
                    if (want != null) p.R.sharedMaterial = want;
                }
            }
            if (p.HTex == null || p.HTex == p.ShownTex) return;
            p.ShownTex = p.HTex;
            Material sm = p.R.sharedMaterial;
            Texture have = sm != null && sm.HasProperty("_MainTex") ? sm.mainTexture : null;
            Texture tex = p.HTex.Length > 0 ? FindTex(p.HTex) : null;
            if (tex != null && tex != have) { Block().Clear(); Block().SetTexture("_MainTex", tex); p.R.SetPropertyBlock(Block()); p.Block = true; }
            else if (p.Block) { Block().Clear(); p.R.SetPropertyBlock(Block()); p.Block = false; }
        }

        static MaterialPropertyBlock block;
        static MaterialPropertyBlock Block() { if (block == null) block = new MaterialPropertyBlock(); return block; }

        static string BaseName(string n)
        {
            while (n.EndsWith(" (Instance)")) n = n.Substring(0, n.Length - 11);
            return n;
        }

        // Ce qu'on en voit chez le joueur qui la tient : vrai si ca a change depuis le dernier envoi.
        static bool Look(Part p)
        {
            bool ch = false;
            Material sm = p.R.sharedMaterial;
            if (sm != p.Mat) { p.Mat = sm; p.MatName = sm != null ? BaseName(sm.name) : ""; p.HasTex = sm != null && sm.HasProperty("_MainTex"); ch = true; }
            Texture tx = p.HasTex ? sm.mainTexture : null;
            if (tx != p.Tex) { p.Tex = tx; p.TexName = tx != null ? tx.name : ""; ch = true; }
            return ch;
        }

        static void SendLocked(Machine m, float now)
        {
            bool full = now >= m.NextFull;
            if (full) m.NextFull = now + 2f;
            NetWriter w = null;
            foreach (Part p in m.Parts)
            {
                if (p.T == null || !p.T.IsChildOf(m.Root)) continue;
                bool act = p.T.gameObject.activeSelf;
                bool moved = Quaternion.Angle(p.T.localRotation, p.Rot) > 0.5f || (p.T.localPosition - p.Pos).sqrMagnitude > 1e-6f;
                bool stopped = p.Spin && !moved;   // rouleau arrete depuis le dernier envoi : son angle final
                if (p.Reel) p.Spin = moved;
                if (moved) p.Moved = true;
                string txt = p.Text != null ? p.Text.text : null;
                bool txtCh = p.Text != null && txt != p.Txt;
                bool lookCh = p.R != null && Look(p);
                bool cam = p.Cam != null && p.Cam.enabled;
                bool camCh = p.Cam != null && cam != p.CamOn;
                if (!full && act == p.Active && !moved && !stopped && !txtCh && !lookCh && !camCh) continue;   // toutes les 2 s : tout
                p.Active = act; p.Pos = p.T.localPosition; p.Rot = p.T.localRotation; p.Txt = txt; p.CamOn = cam;
                bool withPose = moved || stopped || full && (p.Moved || p.Reel);
                bool withLook = p.R != null && (lookCh || full);
                int fl = (act ? 1 : 0) | (withPose ? 2 : 0) | (txt != null ? 4 : 0) | (withLook ? 8 | 16 : 0)
                       | (p.Cam != null ? 32 : 0) | (cam ? 64 : 0) | (p.Spin ? 128 : 0);
                int len = 12 + p.Key.Length + (withPose ? 28 : 0) + (txt != null ? txt.Length * 2 + 2 : 0) + (withLook ? p.MatName.Length + p.TexName.Length + 4 : 0);
                if (w != null && w.Length + len > 900) { Session.SendAll(w, false); w = null; }
                if (w == null) w = new NetWriter(Msg.Machine).U8(Session.LocalId).Str(m.Key);
                w.Str(p.Key).U8(fl);
                if (withPose) w.Vec(p.Pos).Quat(p.Rot);
                if (txt != null) w.Str(txt);
                if (withLook) w.Str(p.MatName).Str(p.TexName);
            }
            if (w != null) Session.SendAll(w, false);
        }

        // Materiaux et textures par nom (releves une fois, puis de nouveau au plus toutes les 10 s si un nom manque).
        static Dictionary<string, Material> matByName;
        static Dictionary<string, Texture> texByName;
        static float namesAt = -100f;

        static void Names(bool again)
        {
            float now = Time.realtimeSinceStartup;
            if (matByName != null && (!again || now - namesAt < 10f)) return;
            namesAt = now;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            matByName = new Dictionary<string, Material>();
            texByName = new Dictionary<string, Texture>();
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(Material)))
            {
                string n = o.name;
                if (n.Length > 0 && !n.EndsWith(" (Instance)") && !matByName.ContainsKey(n)) matByName[n] = (Material)o;
            }
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(Texture)))
            {
                string n = o.name;
                if (n.Length > 0 && !texByName.ContainsKey(n)) texByName[n] = (Texture)o;
            }
            Log.Info("machines : " + matByName.Count + " materiaux et " + texByName.Count + " textures par nom (" + sw.ElapsedMilliseconds + " ms)");
        }

        static Material FindMat(string n)
        {
            Material m;
            Names(false);
            if (!matByName.TryGetValue(n, out m)) { Names(true); matByName.TryGetValue(n, out m); }
            return m;
        }

        static Texture FindTex(string n)
        {
            Texture t;
            Names(false);
            if (!texByName.TryGetValue(n, out t)) { Names(true); texByName.TryGetValue(n, out t); }
            return t;
        }

        // ---------------------------------------------------------------- ordinateur de la maison
        // Jeu lance (enfant actif de COMPUTER/SYSTEM : RAMI-Massacre, Kaappis-Grilli...) : les autres le savent.
        static Transform system;
        static float nextComputer;
        static string playing = "";

        static void Computer(float now)
        {
            if (now < nextComputer) return;
            nextComputer = now + 1f;
            if (system == null)
            {
                GameObject g = GameObject.Find("COMPUTER");   // racine active ; SYSTEM ne l'est que l'ordinateur allume
                system = g != null ? g.transform.Find("SYSTEM") : null;
                if (system == null) { nextComputer = now + 10f; return; }
            }
            string game = "";
            if (system.gameObject.activeInHierarchy)
                foreach (Transform c in system)
                {
                    if (!c.gameObject.activeSelf) continue;
                    string n = c.name;
                    int dash = n.IndexOf('-');
                    if (dash > 0 && dash < n.Length - 1 && c.GetComponent<PlayMakerFSM>() != null) { game = n.Substring(dash + 1); break; }
                }
            if (game == playing) return;
            playing = game;
            Log.Info(game.Length > 0 ? "ordinateur : joue a " + game : "ordinateur : plus de jeu");
            if (Session.RemoteCount > 0) Session.SendAll(new NetWriter(Msg.MachineLock).U8(L_GAME).U8(Session.LocalId).Str(game), true);
        }

        static void Played(int who, string game)
        {
            if (game.Length > 0) Hud.Toast(NameOf(who) + " joue a " + game);
        }

        // ---------------------------------------------------------------- essais
        static Machine Find(string name)
        {
            foreach (Machine m in lockables) if (m.Name == name) return m;
            foreach (Machine m in machines.Values) if (m.Key.EndsWith(name)) return m;
            return null;
        }

        static bool Shown(Transform t, Transform root)
        {
            for (; t != null && t != root; t = t.parent) if (!t.gameObject.activeSelf) return false;
            return true;
        }

        static string TexOf(Part p)
        {
            if (p.Block) return p.ShownTex;
            Material sm = p.R.sharedMaterial;
            Texture t = sm != null && sm.HasProperty("_MainTex") ? sm.mainTexture : null;
            return t != null ? t.name : "-";
        }

        // Etat d'une machine a jeu : verrou, cartes (materiau / texture de la couleur et du rang), textes, rouleaux.
        public static string State(string name)
        {
            Machine m = Find(name);
            if (m == null || m.Root == null) return name + " : pas suivie (" + machines.Count + " machines)";
            var sb = new System.Text.StringBuilder(m.Name);
            sb.Append(" : ").Append(m.L == null ? "sans verrou" : m.L.Owner >= 0 ? "tenue par " + NameOf(m.L.Owner) : "libre");
            if (m.Spectating) sb.Append(", spectateur (").Append(m.Frozen.Count).Append(" automates arretes)");
            if (!m.Root.gameObject.activeInHierarchy) sb.Append(", inactive ici (loin)");
            foreach (Part p in m.Parts)
            {
                if (p.T == null) continue;
                string n = p.T.name;
                bool card = p.R != null && (n.EndsWith("-Suit") || n.EndsWith("-Rank"));
                if (!card && p.Text == null && !p.Reel) continue;
                sb.Append(" | ").Append(n).Append(Shown(p.T, m.Root) ? "" : " (cache)").Append(" = ");
                if (card) sb.Append(p.R.sharedMaterial != null ? BaseName(p.R.sharedMaterial.name) : "-").Append(" / ").Append(TexOf(p));
                else if (p.Text != null) sb.Append('\'').Append(p.Text.text).Append('\'');
                else sb.Append(p.T.localEulerAngles.ToString("F0")).Append(p.Spinning || p.Spin ? " tourne" : "");
            }
            return sb.ToString();
        }

        // Comme un clic du joueur sur un bouton : le verrou d'abord (l'hote decide), puis l'automate 'Use'
        // passe dans l'etat ou mene USE.
        public static string TestPress(string name, string button)
        {
            Machine m = Find(name);
            if (m == null || m.Buttons == null) return name + " : pas suivie";
            PlayMakerFSM f = null;
            foreach (PlayMakerFSM b in m.Buttons) if (b != null && b.gameObject.name == button) { f = b; break; }
            if (f == null) return name + "/" + button + " : bouton introuvable";
            if (!f.gameObject.activeInHierarchy) return name + "/" + button + " : machine inactive ici (trop loin ?)";
            if (!Ask(m)) return name + "/" + button + " refuse : machine occupee par " + NameOf(m.L.Owner) + " (bouton '" + f.ActiveStateName + "', " + (f.enabled ? "actif" : "arrete") + ")";
            m.NextRenew = Time.realtimeSinceStartup + 1f;
            keepMoneyUntil = Time.realtimeSinceStartup + KeepMoney;   // comme Press
            string target = UseTarget(f), before = f.ActiveStateName;
            if (target == null) return name + "/" + button + " : pas de transition USE";
            Game.SetState(f, target);
            return name + "/" + button + " : " + before + " => " + f.ActiveStateName + " (" + target + "), " + (m.L.Owner >= 0 ? "tenue par " + NameOf(m.L.Owner) : "libre");
        }

        static string UseTarget(PlayMakerFSM f)
        {
            FsmState s = f.Fsm.GetState("Wait button");
            if (s == null) return null;
            foreach (FsmTransition tr in s.Transitions) if (tr.EventName == "USE") return tr.ToState;
            return null;
        }

        static int testStep;
        static float testNextLog, testTryAt, testLastAt, testMoney0, testMoneyAt;
        static bool testMoneyBase, testMoneyDone;
        static string[] testSeq, testRefus;

        static float Money()
        {
            FsmFloat c = FsmVariables.GlobalVariables.FindFsmFloat("PlayerMoney");
            return c != null ? c.Value : -1f;
        }

        // [Test] Autotest=machine. Invite (TestPos devant les machines) : boutons [Test] TestBoutons
        // (machine/bouton@t;...) et son liquide apres chaque appui, puis 6 s apres le dernier (TakeWin et
        // Cashout le font monter : gain garde pour soi). Hote : 2 s apres avoir vu la machine [Test] TestRefus
        // prise par un autre, il appuie a son tour (refus attendu) ; son liquide ne doit pas bouger pendant que
        // l'invite joue (verdict quand plus aucune machine n'est tenue par un autre, au plus 35 s apres).
        // Les deux : etat des machines [Test] SuivreMachines toutes les 2 s.
        // Essai 'pin' : l'hote (TestPos devant la pompe) entre un code au terminal comme au clavier du jeu (PIN puis un
        // PINTYPE par chiffre, PINadd = le chiffre) ; les deux cotes notent le texte du code toutes les secondes.
        static float pinNext; static int pinStep;
        static void TestPin(float t)
        {
            Machine m = null;
            foreach (Machine x in machines.Values) if (x.Name.StartsWith("FuelPumps_")) m = x;
            if (m == null) return;
            Part code = null;
            foreach (Part p in m.Parts) if (p.Ext && p.Text != null && p.T.name == "PINcode") code = p;
            if (t > 30f && t < 60f && t >= pinNext)
            {
                pinNext = t + 1f;
                Log.Info("autotest : pin, ecran " + (code != null ? "'" + code.Text.text + "' actif " + code.T.gameObject.activeInHierarchy + (code.Held ? " (recu)" : "") : "sans texte du code"));
            }
            if (!Session.IsHost) return;
            PlayMakerFSM logic = null;
            foreach (PlayMakerFSM f in m.Root.GetComponentsInChildren<PlayMakerFSM>(true)) if (f.FsmName == "Logic" && f.FsmVariables.FindFsmString("PINadd") != null) logic = f;
            if (logic == null || !logic.gameObject.activeInHierarchy) { if (t > 34f && pinStep == 0) { pinStep = -1; Log.Info("autotest : pin, terminal eteint ici (TestPos devant la pompe)"); } return; }
            if (pinStep >= 0 && pinStep < 5 && t > 36f + pinStep * 2f)
            {
                lastInput = Time.realtimeSinceStartup;   // comme une touche du clavier
                if (pinStep == 0) logic.SendEvent("PIN");
                else { logic.FsmVariables.FindFsmString("PINadd").Value = "" + pinStep; logic.SendEvent("PINTYPE"); }
                Log.Info("autotest : pin, " + (pinStep == 0 ? "saisie du code" : "chiffre " + pinStep) + " -> " + logic.ActiveStateName);
                pinStep++;
            }
        }

        public static void Test(string mode, float t)
        {
            if (mode == "pin") { TestPin(t); return; }
            if (mode != "machine") return;
            if (t > 20f && t >= testNextLog)
            {
                testNextLog = t + 2f;
                foreach (string n in Config.Get("Test", "SuivreMachines", "VideoPoker;SlotMachine;SlotMachinePub").Split(';')) Log.Info("autotest : " + State(n));
            }
            if (!Session.IsHost)
            {
                if (testSeq == null) testSeq = Config.Get("Test", "TestBoutons", "VideoPoker/InsertCoin@40;VideoPoker/TakeWin@42;VideoPoker/InsertCoin@48;VideoPoker/Deal@50;"
                                                          + "SlotMachine/PayMoney@53;SlotMachine/Start@54.5;SlotMachine/Cashout@60").Split(';');
                if (testStep == testSeq.Length && t >= testLastAt + 6f) { testStep++; Log.Info("autotest : liquide de l'invite apres les machines : " + Money()); }
                if (testStep >= testSeq.Length) return;
                string[] it = testSeq[testStep].Split('@');
                float at = it.Length > 1 ? float.Parse(it[1], System.Globalization.CultureInfo.InvariantCulture) : 40f;
                if (t < at) return;
                testStep++; testLastAt = t;
                string[] mb = it[0].Split('/');
                Log.Info("autotest : appuie " + (mb.Length > 1 ? TestPress(mb[0], mb[1]) : it[0] + " ?") + " ; liquide " + Money());
                return;
            }
            HostMoney(t);
            if (testRefus == null) testRefus = Config.Get("Test", "TestRefus", "VideoPoker/Deal").Split('/');
            if (testRefus.Length < 2) return;
            Machine m = Find(testRefus[0]);
            if (testStep == 0 && m != null && m.L != null && m.L.Owner >= 0 && m.L.Owner != Session.LocalId) { testStep = 1; testTryAt = t + 2f; }
            if (testStep == 1 && t >= testTryAt) { testStep = 2; Log.Info("autotest : l'hote essaie " + TestPress(testRefus[0], testRefus[1]) + " ; " + State(testRefus[0])); }
        }

        // Hote : son liquide quand un autre prend une machine, puis quand plus aucune n'est tenue par un autre
        // (au plus 35 s apres) : inchange attendu, les gains de l'invite restent a l'invite.
        static void HostMoney(float t)
        {
            if (testMoneyDone) return;
            bool other = false;
            foreach (Machine x in lockables) if (x.L.Owner >= 0 && x.L.Owner != Session.LocalId) other = true;
            if (!testMoneyBase)
            {
                if (!other) return;
                testMoneyBase = true; testMoney0 = Money(); testMoneyAt = t;
                Log.Info("autotest : liquide de l'hote avant les gains de l'invite : " + testMoney0);
                return;
            }
            if (other && t < testMoneyAt + 35f) return;
            testMoneyDone = true;
            float now = Money(), d = now - testMoney0;
            Log.Info("autotest : liquide de l'hote apres les gains de l'invite : " + testMoney0 + " -> " + now
                     + (Mathf.Abs(d) < 0.5f ? ", inchange (OK)" : ", ECART " + d + " (gain de machine partage ?)"));
        }
    }
}
