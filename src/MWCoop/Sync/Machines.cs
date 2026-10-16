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
    //  - Ordinateur de la maison : chacun joue chez lui ; les autres voient "X joue a Massacre", et son ecran
    //    allume avec le programme a l'ecran (copie visuelle, automates coupes : voir "ordinateur de la maison" plus bas).
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
        const int L_FREE = 0, L_ASK = 1, L_HELD = 2, L_REFUSED = 3, L_GAME = 4, L_POWER = 5;   // Msg.MachineLock

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
            computerOn = false; compDiffAt = 0f; nextPowerSend = 0f; shownRemote = -1;
            compBase.Clear(); compBaseMat.Clear(); screen.Clear(); remoteOn.Clear();
            if (mirroring && screenMat != null && offTex != null) screenMat.SetTexture("_MainTex", offTex);   // (materiau du jeu : il survit au chargement)
            program = ""; named = 0; namedTries = 0; namedRetry = 0f; powerFsm = partsFsm = null; lights = null;
            screenMat = null; videoTex = offTex = null; mirroring = false; mirrorProg = null; mirrorWho = -1;
            muted.Clear(); mirrorSaved.Clear(); compTest = 0; compTestNext = 0f;
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
                    // Pompe : zones cliquables invisibles (fente de la carte, billets, boutons de l'ecran : collider + automate,
                    // pas de rendu) a la logique de chacun -- allumees / eteintes d'apres un autre joueur, la carte d'un invite
                    // n'entrait plus (retour de Menor, 11/10 : l'hote a cote de lui, ses touches en faisaient « l'utilisateur »).
                    if (!m.Lockable && c.GetComponent<Collider>() != null && c.GetComponent<Renderer>() == null && c.GetComponent<PlayMakerFSM>() != null) continue;
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
            if (mirroring && (!Session.Active || !PlayerSync.InGame)) StopMirror(GameWantsOn());   // fin de partie : l'ecran copie est rendu
            if (!Session.Active || nextScan < 0 || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextScan) { nextScan = machines.Count == 0 ? now + 30f : float.MaxValue; Scan(); }
            Computer(now);
            if (machines.Count == 0) return;
            if (player == null) { GameObject g = Game.PlayerGo; if (g == null) return; player = g.transform; }
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || CodeKey()) lastInput = now;   // (pas les deplacements : se servir de la machine)
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

        // Touche du code ou du montant (chiffres, Entree, effacer).
        static bool CodeKey()
        {
            if (!Input.anyKeyDown) return false;
            for (KeyCode k = KeyCode.Alpha0; k <= KeyCode.Alpha9; k++) if (Input.GetKeyDown(k)) return true;
            for (KeyCode k = KeyCode.Keypad0; k <= KeyCode.Keypad9; k++) if (Input.GetKeyDown(k)) return true;
            return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Backspace);
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
            float now0 = Time.realtimeSinceStartup;
            Machine m;
            machines.TryGetValue(key, out m);
            // Machine a jeu : seulement ce qu'envoie celui qui la tient (un envoi en retard de l'ancien ne compte pas).
            bool take = m == null || !m.Lockable || !m.Spectating || who == m.L.Owner;
            // Pompe dont le joueur d'ici se sert lui-meme (clic ou code dans les 15 s) : sa logique l'emporte ici.
            if (m != null && !m.Lockable && now0 < m.OwnerUntil) take = false;
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
            if (m != null && take) m.OwnerUntil = 0f;   // un autre s'en sert : on n'envoie pas en meme temps
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
                    if (near && !m.Toasted) Hud.Toast(Lang.T("Machine occup\u00E9e par ", "Machine in use by ") + NameOf(l.Owner));
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
            if (m.L.Owner >= 0 && m.L.Owner != Session.LocalId) { Hud.Toast(Lang.T("Machine occup\u00E9e par ", "Machine in use by ") + NameOf(m.L.Owner)); return false; }
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
                if (who == Session.LocalId) { Hud.Toast(Lang.T("Machine occup\u00E9e par ", "Machine in use by ") + NameOf(l.Owner)); return false; }
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
            remoteOn.Remove(pi.Id);   // son ordinateur ne compte plus (ecran recopie ici)
            var keys = new List<string>();
            foreach (KeyValuePair<string, Lock> kv in locks)
                if (kv.Value.Owner >= 0 && (kv.Value.Owner == pi.Id || !Session.IsHost && pi.Id == 0)) keys.Add(kv.Key);
            foreach (string k in keys)
            {
                if (Session.IsHost) HostRelease(pi.Id, k, pi.Name + " est parti");
                else SetOwner(k, -1, pi.Name + " est parti");
            }
        }

        // Msg.MachineLock : U8 operation, U8 joueur, Str machine (jeu de l'ordinateur pour L_GAME ; pour L_POWER "0" eteint,
        // "1" allume a l'invite DOS, "1|<enfant de COMPUTER/SYSTEM>" allume sur ce programme : "1|Kaappis-Fishgame", "1|TELEBBS").
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
                else if (op == L_POWER) { Session.Broadcast(new NetWriter(Msg.MachineLock).U8(L_POWER).U8(who).Str(key), true, who); Powered(who, key); }
                return;
            }
            if (op == L_HELD) SetOwner(key, who, "hote");
            else if (op == L_FREE) { if (GetLock(key).Owner == who) SetOwner(key, -1, "hote"); }
            else if (op == L_REFUSED) { SetOwner(key, who, "refusee par l'hote"); Hud.Toast(Lang.T("Machine occup\u00E9e par ", "Machine in use by ") + NameOf(who)); }
            else if (op == L_GAME) Played(who, key);
            else if (op == L_POWER) Powered(who, key);
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
        // Jeu lance (enfant actif de COMPUTER/SYSTEM : RAMI-Massacre, Kaappis-Grilli...) : les autres le savent (L_GAME).
        // Allumage : annonce (L_POWER "1|<programme>" / "0", renvoye toutes les 10 s tant qu'il est allume, pour un arrivant,
        // et tout de suite quand le programme affiche change), et l'ecran allume se voit chez les autres (relecture du 15/10,
        // ecart n°12). La partie reste a chacun (COMPUTER hors du monde : WorldFsms.SkipRoots).
        // Pieces NOMMEES d'apres le releve du jeu (ref v.261008-01, GAME-complet.txt) :
        //  - interrupteur : <386CASE>/Functions/ButtonPower, automate 'Use' (FsmBool PowerOn ; etats ON / OFF, et ON 2 / OFF 2
        //    a l'appartement). ON : materiau 386_screen._MainTex = 386_video (texture de rendu de COMPUTER/SYSTEM/Camera),
        //    active <386CASE>/Functions/Lights (LCD, ScreenLight, monitor_led) puis COMPUTER/SYSTEM ; OFF : tv_off, eteint les
        //    deux. Le jeu n'allume que si PowerOn, courant (HouseElectricity + Fuse, ou HouseElectricity2 + FuseApartment a
        //    l'appartement) et COMPUTER/Computer 'Parts' Case / Monitor / Peripherals (GameWantsOn refait ce test).
        //    <386CASE> : FleaMarketProducts/Disable/386CASE au releve, range sous COMPUTER/PosHome/TRI_Case (ou PosApart)
        //    une fois pose -- trouve par son nom et celui de l'automate, pas par le chemin.
        //  - programme affiche : enfant actif de COMPUTER/SYSTEM -- Kaappis-* / RAMI-* / Rami-Turku / PROCYON-ProPilkki
        //    (actives par SYSTEM/POS/Command 'Typer', etats Grilli, Fishgame, Massacre...), TELEBBS (modem) ; aucun :
        //    l'invite DOS (SYSTEM/POS : BIOS, Text, dos_bg, Command).
        // Chez les autres, ordinateur eteint ici et un joueur ayant allume le sien : copie VISUELLE de son ecran. Tous les
        // automates, sons et animations sous SYSTEM sont coupes (et les automates de Lights : Consumption compterait
        // l'electricite, Fuseblow ferait sauter le fusible), puis SYSTEM, Lights et SON programme sont actives (POS et les
        // autres programmes caches) et 386_screen prend 386_video : la camera filme ce programme tel qu'il est, immobile.
        // Rien n'est joue ni compte ici, chacun garde ses parties. Fin de la copie (plus personne, ou on allume le sien :
        // PowerOn + courant + pieces, comme le jeu) : SYSTEM eteint, chaque objet reprend son etat d'avant, automates
        // rendus ; si le jeu voulait l'allumer, SYSTEM et Lights sont rallumes (ses automates repartent comme a l'allumage).
        // Repli : piece nommee introuvable (autre version du jeu) -> l'ancien apprentissage : ce qui change sous COMPUTER
        // hors SYSTEM 3 s apres notre basculement est "l'ecran" ("ordinateur : ecran = ..."), recopie allume / eteint.
        class ScreenPart { public Transform T; public bool OnActive, OffActive; public Renderer R; public Material OnMat, OffMat; }
        static Transform system;
        static float nextComputer, compDiffAt, nextPowerSend;
        static string playing = "", program = "";   // program : enfant de SYSTEM affiche ici ("" : DOS)
        static bool computerOn;
        static int shownRemote = -1;   // (repli) ecran mis ici a l'etat d'un autre joueur : 1 allume, 0 eteint, -1 le jeu a la main
        static readonly Dictionary<Transform, bool> compBase = new Dictionary<Transform, bool>();
        static readonly Dictionary<Renderer, Material> compBaseMat = new Dictionary<Renderer, Material>();
        static readonly List<ScreenPart> screen = new List<ScreenPart>();
        static readonly Dictionary<int, string> remoteOn = new Dictionary<int, string>();   // joueurs dont l'ordinateur est allume -> programme
        // pieces nommees : 0 pas encore cherchees, 1 trouvees, -1 introuvables (apprentissage ; nouvel essai 30 s plus tard, 3 en tout)
        static int named, namedTries;
        static float namedRetry;
        static PlayMakerFSM powerFsm, partsFsm;
        static GameObject lights;
        static Material screenMat;
        static Texture videoTex, offTex;
        // copie de l'ecran d'un autre joueur
        static bool mirroring;
        static string mirrorProg;
        static int mirrorWho = -1;
        static readonly List<Behaviour> muted = new List<Behaviour>();
        static readonly Dictionary<GameObject, bool> mirrorSaved = new Dictionary<GameObject, bool>();

        static void Computer(float now)
        {
            if (compDiffAt > 0f && now >= compDiffAt) LearnScreen();
            if (now < nextComputer) return;
            nextComputer = now + 1f;
            if (system == null)
            {
                GameObject g = GameObject.Find("COMPUTER");   // racine active ; SYSTEM ne l'est que l'ordinateur allume
                system = g != null ? g.transform.Find("SYSTEM") : null;
                if (system == null) { nextComputer = now + 10f; return; }
            }
            if (named == 0 || named < 0 && namedTries < 3 && now >= namedRetry) FindNamed(now);
            // Copie en cours : SYSTEM est actif par nous, l'allumage d'ici se lit sur l'interrupteur (comme le jeu le decide).
            bool on = mirroring ? GameWantsOn() : system.gameObject.activeInHierarchy;
            if (on != computerOn)
            {
                computerOn = on;
                Log.Info(on ? "ordinateur : allume ici" : "ordinateur : eteint ici");
                if (on && mirroring) StopMirror(true);         // on allume le sien : le jeu reprend la main
                if (Session.RemoteCount > 0) SendPower(now);
                if (named < 0 && compDiffAt <= 0f) compDiffAt = now + 3f;   // (repli) les pieces de l'ecran : ce qui aura change d'ici 3 s
                shownRemote = -1;                              // le jeu a la main sur l'ecran ici
            }
            else
            {
                if (on && now >= nextPowerSend && Session.RemoteCount > 0) SendPower(now);
                if (named < 0 && compDiffAt <= 0f) SnapshotComputer();
            }
            if (named > 0) MirrorTick(on); else ApplyRemoteScreen(on);
            string prog = on ? ActiveProgram() : "";
            if (prog != program)
            {
                program = prog;
                if (on && Session.RemoteCount > 0)
                {
                    SendPower(now);
                    Log.Info("ordinateur : programme " + (prog.Length > 0 ? prog : "DOS (POS)") + " affiche chez les autres");
                }
            }
            int dash = prog.IndexOf('-');
            string game = dash > 0 ? prog.Substring(dash + 1) : "";
            if (game == playing) return;
            playing = game;
            Log.Info(game.Length > 0 ? "ordinateur : joue a " + game : "ordinateur : plus de jeu");
            if (Session.RemoteCount > 0) Session.SendAll(new NetWriter(Msg.MachineLock).U8(L_GAME).U8(Session.LocalId).Str(game), true);
        }

        // Programme de SYSTEM : un enfant « Editeur-Jeu » porteur d'automate, ou TELEBBS (le modem).
        static bool IsProgram(Transform c)
        {
            string n = c.name;
            if (n == "TELEBBS") return true;
            int dash = n.IndexOf('-');
            return dash > 0 && dash < n.Length - 1 && c.GetComponent<PlayMakerFSM>() != null;
        }

        static string ActiveProgram()
        {
            string bbs = "";
            foreach (Transform c in system)
            {
                if (!c.gameObject.activeSelf || !IsProgram(c)) continue;
                if (c.name == "TELEBBS") bbs = c.name; else return c.name;   // (un jeu lance depuis le modem l'emporte)
            }
            return bbs;
        }

        static void SendPower(float now)
        {
            nextPowerSend = now + 10f;
            string s = computerOn ? (program.Length > 0 ? "1|" + program : "1") : "0";
            Session.SendAll(new NetWriter(Msg.MachineLock).U8(L_POWER).U8(Session.LocalId).Str(s), true);
        }

        static void Played(int who, string game)
        {
            if (game.Length > 0) Hud.Toast(NameOf(who) + Lang.T(" joue à ", " plays ") + game);
        }

        static void Powered(int who, string key)
        {
            bool on = key.Length > 0 && key[0] == '1';
            string prog = on && key.Length > 2 && key[1] == '|' ? key.Substring(2) : "";
            string was;
            bool had = remoteOn.TryGetValue(who, out was);
            if (on) remoteOn[who] = prog; else remoteOn.Remove(who);
            if (had == on && (!on || was == prog)) return;   // renvoi des 10 s : rien de neuf
            Log.Info("ordinateur de " + NameOf(who) + (on ? " allume" + (prog.Length > 0 ? ", programme " + prog : ", DOS") : " eteint")
                     + (named < 0 && screen.Count == 0 ? " (pieces de l'ecran pas encore connues ici)" : ""));
        }

        // ---- pieces nommees (releve du jeu)
        static void FindNamed(float now)
        {
            namedTries++; namedRetry = now + 30f;
            powerFsm = null;
            foreach (Object o in Game.FsmsNamed("Use"))
            {
                var f = o as PlayMakerFSM;
                if (f == null || f.hideFlags != HideFlags.None || f.gameObject.name != "ButtonPower") continue;
                Transform fn = f.transform.parent;
                if (fn == null || fn.name != "Functions" || fn.parent == null || fn.parent.name != "386CASE" || f.FsmVariables.FindFsmBool("PowerOn") == null) continue;
                if (powerFsm == null || f.transform.root.gameObject.activeInHierarchy) powerFsm = f;
            }
            Transform lt = powerFsm != null ? powerFsm.transform.parent.Find("Lights") : null;
            lights = lt != null ? lt.gameObject : null;
            GameObject comp = Game.FindAny("COMPUTER/Computer");
            partsFsm = comp != null ? Game.FsmOn(comp, "Parts") : null;
            screenMat = FindMat("386_screen"); videoTex = FindTex("386_video"); offTex = FindTex("tv_off");
            named = powerFsm != null && screenMat != null && videoTex != null ? 1 : -1;
            if (named > 0)
            {
                Log.Info("ordinateur : pieces nommees trouvees (interrupteur " + Recon.Path(powerFsm.transform) + ", Lights " + (lights != null ? "oui" : "NON")
                         + ", Parts " + (partsFsm != null ? "oui" : "NON") + ", 386_screen / 386_video / tv_off " + (offTex != null ? "oui" : "sans tv_off") + ") : ecran recopie sans apprentissage");
                compDiffAt = 0f; screen.Clear(); compBase.Clear(); compBaseMat.Clear();
                return;
            }
            var miss = new List<string>();
            if (powerFsm == null) miss.Add("386CASE/Functions/ButtonPower 'Use' (PowerOn)");
            if (screenMat == null) miss.Add("materiau 386_screen");
            if (videoTex == null) miss.Add("texture 386_video");
            Log.Info("ordinateur : pieces nommees introuvables (" + string.Join(", ", miss.ToArray()) + ") : repli sur l'apprentissage de l'ecran (essai " + namedTries + "/3)");
        }

        static bool PartsBool(string name, bool def)
        {
            FsmBool b = partsFsm != null ? partsFsm.FsmVariables.FindFsmBool(name) : null;
            return b != null ? b.Value : def;
        }

        // Le jeu allumerait-il l'ordinateur ici ? (ButtonPower 'Use' : Press -> Switch On -> State 1 -> ON.)
        static bool GameWantsOn()
        {
            if (powerFsm == null) return false;
            FsmBool p = powerFsm.FsmVariables.FindFsmBool("PowerOn");
            if (p == null || !p.Value) return false;
            if (!PartsBool("Case", true) || !PartsBool("Monitor", true) || !PartsBool("Peripherals", true)) return false;
            bool apart = PartsBool("Apartment", false);
            FsmBool elec = FsmVariables.GlobalVariables.FindFsmBool(apart ? "HouseElectricity2" : "HouseElectricity");
            FsmBool fuse = powerFsm.FsmVariables.FindFsmBool(apart ? "FuseApartment" : "Fuse");
            return (elec == null || elec.Value) && (fuse == null || fuse.Value);
        }

        // Chaque seconde : copier l'ecran d'un autre joueur (le plus petit numero parmi ceux qui l'ont allume) quand le
        // sien est eteint et l'ecran pose ici ; sinon rendre la main au jeu.
        static void MirrorTick(bool localOn)
        {
            int who = -1; string prog = null;
            bool placed = PartsBool("Case", true) && PartsBool("Monitor", true) && (partsFsm == null || partsFsm.gameObject.activeSelf);   // (COMPUTER/Computer : active a la pose de l'unite)
            if (!localOn && placed && !GameWantsOn())
                foreach (KeyValuePair<int, string> kv in remoteOn) if (who < 0 || kv.Key < who) { who = kv.Key; prog = kv.Value; }
            if (who < 0) { if (mirroring) StopMirror(localOn); return; }
            if (!mirroring) StartMirror();
            else if (!system.gameObject.activeSelf || lights != null && !lights.activeSelf)
            {
                // coupure de courant ici (ELEC_CUTOFF : etat OFF du jeu) pendant la copie : on la remet
                system.gameObject.SetActive(true);
                if (lights != null) lights.SetActive(true);
                if (screenMat != null && videoTex != null) screenMat.SetTexture("_MainTex", videoTex);
                mirrorProg = null;   // (OFF eteint aussi SYSTEM/TELEBBS : le programme est remontre)
            }
            if (prog != mirrorProg || who != mirrorWho) ShowProgram(who, prog);
        }

        static void SetSaved(GameObject g, bool active)
        {
            if (!mirrorSaved.ContainsKey(g)) mirrorSaved[g] = g.activeSelf;
            if (g.activeSelf != active) g.SetActive(active);
        }

        static void StartMirror()
        {
            mirroring = true; mirrorProg = null; mirrorWho = -1;
            muted.Clear(); mirrorSaved.Clear();
            // Tout ce qui joue sous SYSTEM (automates, sons, animations) coupe AVANT l'activation : rien ne demarre, seule la
            // camera tourne. (Les automates jamais actives recoivent leur Awake, pas de Start ni d'OnEnable.)
            foreach (Behaviour b in system.GetComponentsInChildren<Behaviour>(true))
                if (b != null && b.enabled && (b is PlayMakerFSM || b is AudioSource || b is Animation)) { b.enabled = false; muted.Add(b); }
            if (lights != null)
                foreach (PlayMakerFSM f in lights.GetComponents<PlayMakerFSM>()) if (f.enabled) { f.enabled = false; muted.Add(f); }
            foreach (Transform c in system) if (c.gameObject.activeSelf && IsProgram(c)) SetSaved(c.gameObject, false);   // restes d'une partie d'ici
            SetSaved(system.gameObject, true);
            if (lights != null) SetSaved(lights, true);
            if (screenMat != null && videoTex != null) screenMat.SetTexture("_MainTex", videoTex);
        }

        static void ShowProgram(int who, string prog)
        {
            if (!string.IsNullOrEmpty(mirrorProg) && mirrorProg != prog)
            {
                Transform old = system.Find(mirrorProg);
                if (old != null) SetSaved(old.gameObject, false);
            }
            Transform c = prog.Length > 0 ? system.Find(prog) : null;
            if (c != null) SetSaved(c.gameObject, true);
            Transform pos = system.Find("POS");
            if (pos != null) SetSaved(pos.gameObject, c == null);   // l'invite DOS seulement sans programme
            mirrorProg = prog; mirrorWho = who;
            Log.Info("ordinateur : ecran de " + NameOf(who) + " recopie ici, programme " + (c != null ? prog : prog.Length > 0 ? prog + " introuvable ici (DOS)" : "DOS (POS)")
                     + " (" + muted.Count + " automates / sons / animations coupes)");
        }

        static void StopMirror(bool gameOn)
        {
            mirroring = false; mirrorProg = null; mirrorWho = -1;
            GameObject sys = system != null ? system.gameObject : null;
            bool sysWas = false, lightsWas = false;
            if (sys != null) { mirrorSaved.TryGetValue(sys, out sysWas); sys.SetActive(false); }   // tout arrete avant de rendre les automates
            if (lights != null) { mirrorSaved.TryGetValue(lights, out lightsWas); lights.SetActive(false); }
            foreach (KeyValuePair<GameObject, bool> kv in mirrorSaved)
                if (kv.Key != null && kv.Key != sys && kv.Key != lights && kv.Key.activeSelf != kv.Value) kv.Key.SetActive(kv.Value);
            foreach (Behaviour b in muted) if (b != null) b.enabled = true;
            int n = muted.Count;
            muted.Clear(); mirrorSaved.Clear();
            // Comme l'etat ON du jeu : Lights puis SYSTEM (ses automates repartent a l'activation).
            if (lights != null && (gameOn || lightsWas)) lights.SetActive(true);
            if (sys != null && (gameOn || sysWas)) sys.SetActive(true);
            if (screenMat != null) { Texture t = gameOn ? videoTex : offTex; if (t != null) screenMat.SetTexture("_MainTex", t); }
            Log.Info("ordinateur : copie de l'ecran rendue (" + n + " automates / sons / animations rendus" + (gameOn ? ", allume ici : le jeu reprend la main)" : ", ecran eteint ici)"));
        }

        // ---- repli : apprentissage (pieces nommees introuvables)
        // Tout ce qui est sous COMPUTER hors SYSTEM (petit : SYSTEM porte les jeux).
        static List<Transform> ComputerParts()
        {
            var l = new List<Transform>();
            Transform root = system != null ? system.parent : null;
            if (root == null) return l;
            foreach (Transform c in root)
            {
                if (c == system) continue;
                foreach (Transform t in c.GetComponentsInChildren<Transform>(true)) l.Add(t);
            }
            return l;
        }

        // Etat de reference (chaque seconde, hors fenetre d'apprentissage) : ce a quoi comparer apres un basculement.
        static void SnapshotComputer()
        {
            compBase.Clear(); compBaseMat.Clear();
            foreach (Transform t in ComputerParts())
            {
                compBase[t] = t.gameObject.activeSelf;
                Renderer r = t.GetComponent<Renderer>();
                if (r != null) compBaseMat[r] = r.sharedMaterial;
            }
        }

        // 3 s apres notre basculement : ce qui a change par rapport a la reference, c'est l'ecran (etat allume / eteint).
        static void LearnScreen()
        {
            compDiffAt = 0f;
            if (system == null || compBase.Count == 0 || named > 0) return;
            bool on = computerOn;
            int added = 0;
            foreach (Transform t in ComputerParts())
            {
                bool was, act = t.gameObject.activeSelf;
                bool actCh = compBase.TryGetValue(t, out was) && was != act;
                Renderer r = t.GetComponent<Renderer>();
                Material wasMat = null;
                bool matCh = r != null && compBaseMat.TryGetValue(r, out wasMat) && wasMat != r.sharedMaterial;
                if (!actCh && !matCh) continue;
                ScreenPart p = null;
                foreach (ScreenPart x in screen) if (x.T == t) { p = x; break; }
                if (p == null) { p = new ScreenPart { T = t, R = r, OnActive = act, OffActive = act }; screen.Add(p); added++; }
                if (actCh) { if (on) { p.OnActive = act; p.OffActive = was; } else { p.OffActive = act; p.OnActive = was; } }
                if (matCh) { if (on) { p.OnMat = r.sharedMaterial; p.OffMat = wasMat; } else { p.OffMat = r.sharedMaterial; p.OnMat = wasMat; } }
            }
            if (added == 0 && screen.Count == 0) { Log.Info("ordinateur : rien ne change sous COMPUTER hors SYSTEM a l'allumage : ecran non recopie (repli, pieces nommees introuvables)"); return; }
            var sb = new System.Text.StringBuilder("ordinateur : ecran = ");
            foreach (ScreenPart p in screen)
                sb.Append(Recon.Path(p.T)).Append(" (actif ").Append(p.OnActive).Append('/').Append(p.OffActive)
                  .Append(p.OnMat != null || p.OffMat != null ? ", materiau " + (p.OnMat != null ? BaseName(p.OnMat.name) : "-") + "/" + (p.OffMat != null ? BaseName(p.OffMat.name) : "-") : "").Append(") ");
            Log.Info(sb.ToString());
        }

        // (Repli) ordinateur eteint ici : l'ecran prend l'etat d'un autre joueur (allume si l'un l'a allume).
        static void ApplyRemoteScreen(bool localOn)
        {
            if (localOn || compDiffAt > 0f || screen.Count == 0) return;   // le jeu a la main, ou on apprend encore
            int want = remoteOn.Count > 0 ? 1 : 0;
            if (want == shownRemote) return;
            shownRemote = want;
            foreach (ScreenPart p in screen)
            {
                if (p.T == null) continue;
                bool act = want == 1 ? p.OnActive : p.OffActive;
                if (p.T.gameObject.activeSelf != act) p.T.gameObject.SetActive(act);
                Material m = want == 1 ? p.OnMat : p.OffMat;
                if (p.R != null && m != null && p.R.sharedMaterial != m) p.R.sharedMaterial = m;
            }
            Log.Info(want == 1 ? "ordinateur : ecran allume ici (un autre joueur a allume le sien)" : "ordinateur : ecran eteint ici");
        }

        // Etat de l'ordinateur pour les essais.
        static string ComputerState()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(system == null ? "SYSTEM introuvable" : "SYSTEM " + (system.gameObject.activeSelf ? "actif" : "inactif"));
            sb.Append(", pieces nommees ").Append(named > 0 ? "oui" : named < 0 ? "non (repli, " + screen.Count + " pieces apprises)" : "pas encore cherchees");
            FsmBool p = powerFsm != null ? powerFsm.FsmVariables.FindFsmBool("PowerOn") : null;
            sb.Append(", allume ici ").Append(computerOn).Append(" (PowerOn ").Append(p != null ? p.Value.ToString() : "-")
              .Append(", interrupteur ").Append(powerFsm != null ? powerFsm.ActiveStateName : "-").Append(")");
            sb.Append(", programme ici '").Append(program).Append("'");
            sb.Append(", copie ").Append(mirroring ? "de " + NameOf(mirrorWho) + " programme '" + mirrorProg + "' (" + muted.Count + " coupes)" : "non");
            sb.Append(", Lights ").Append(lights != null ? (lights.activeInHierarchy ? "allume" : "eteint") : "-");
            Texture tx = screenMat != null ? screenMat.GetTexture("_MainTex") : null;
            sb.Append(", 386_screen = ").Append(tx != null ? tx.name : "-");
            sb.Append(", enfants actifs de SYSTEM :");
            if (system != null) foreach (Transform c in system) if (c.gameObject.activeSelf) sb.Append(' ').Append(c.name);
            sb.Append(", allumes ailleurs :");
            foreach (KeyValuePair<int, string> kv in remoteOn) sb.Append(' ').Append(NameOf(kv.Key)).Append('=').Append(kv.Value.Length > 0 ? kv.Value : "DOS");
            return sb.ToString();
        }

        // Nasse (EQUIPMENTS/fish trap(itemx)) : au releve du 08/10 le jeu n'a aucune prise -- un seul automate 'Save'
        // (pose sauvegardee), des maillages et HatchPivot (Animation que rien ne joue ; le son fishtrap_open n'est appele
        // par aucun automate). Seule sa pose compte, et Props la suit (cle w:EQUIPMENTS/fish trap(itemx)#rang). Les essais
        // notent sa pose et l'etat de 'Save' des deux cotes.
        static string FishTrapState()
        {
            GameObject g = Game.FindAny("EQUIPMENTS/fish trap(itemx)");
            if (g == null) return "nasse introuvable sous EQUIPMENTS (deplacee ou tenue ?)";
            PlayMakerFSM f = Game.FsmOn(g, "Save");
            var sb = new System.Text.StringBuilder("nasse ");
            sb.Append(g.transform.position.ToString("F2")).Append(", active ").Append(g.activeInHierarchy).Append(", 'Save' ").Append(f != null ? f.ActiveStateName : "-");
            int fsms = g.GetComponentsInChildren<PlayMakerFSM>(true).Length;
            sb.Append(", ").Append(fsms).Append(" automate(s) en tout");
            return sb.ToString();
        }

        // [Test] Autotest=ordinateur. Hote : a 25 s presse l'interrupteur comme un clic (ButtonPower 'Use' -> Press, ou
        // Press 2 a l'appartement), a 45 s lance la peche (SYSTEM/POS/Command 'Typer' -> Fishgame) si l'invite DOS est
        // prete ; etat de l'ordinateur toutes les 5 s de 30 a 60 s. Invite : etat toutes les 5 s de 40 a 70 s (copie de
        // l'ecran de l'hote, programme). Les deux : la nasse a 50 s (hote) / 60 s (invite).
        static int compTest;
        static float compTestNext;
        static void TestComputer(float t)
        {
            bool host = Session.IsHost;
            if (host && compTest == 0 && t >= 25f)
            {
                compTest = 1;
                if (system != null && named == 0) FindNamed(Time.realtimeSinceStartup);
                FsmBool p = powerFsm != null ? powerFsm.FsmVariables.FindFsmBool("PowerOn") : null;
                if (powerFsm == null) Log.Info("autotest ordinateur : interrupteur 386CASE/Functions/ButtonPower introuvable");
                else if (!powerFsm.enabled || !powerFsm.gameObject.activeInHierarchy) Log.Info("autotest ordinateur : interrupteur inactif ici (" + Recon.Path(powerFsm.transform) + " : ordinateur pas achete ou pas pose ?)");
                else if (p != null && p.Value) Log.Info("autotest ordinateur : deja sous tension (PowerOn), rien a presser");
                else
                {
                    string press = PartsBool("Apartment", false) ? "Press 2" : "Press";
                    Game.SetState(powerFsm, press);
                    Log.Info("autotest ordinateur : l'hote presse l'interrupteur (" + press + ") -> " + powerFsm.ActiveStateName + ", PowerOn " + (p != null && p.Value));
                }
            }
            if (host && compTest == 1 && t >= 45f)
            {
                compTest = 2;
                Transform cmd = system != null ? system.Find("POS/Command") : null;
                PlayMakerFSM typer = cmd != null ? Game.FsmOn(cmd.gameObject, "Typer") : null;
                if (typer == null || !cmd.gameObject.activeInHierarchy) Log.Info("autotest ordinateur : invite DOS pas prete (Command " + (cmd == null ? "introuvable" : "inactif") + ", systeme installe ?) : peche non lancee");
                else { Game.SetState(typer, "Fishgame"); Log.Info("autotest ordinateur : l'hote lance la peche -> Typer " + typer.ActiveStateName); }
            }
            float from = host ? 30f : 40f;
            if (t >= from && t <= from + 30f && t >= compTestNext)
            {
                compTestNext = t + 5f;
                Log.Info("autotest ordinateur : " + ComputerState());
            }
            if (compTest < 3 && t >= (host ? 50f : 60f))
            {
                compTest = 3;
                Log.Info("autotest ordinateur : " + FishTrapState());
            }
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
            if (mode == "ordinateur") { TestComputer(t); return; }
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
