using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // PNJ du monde (vendeurs, caissieres, clients, Teimo, Fleetari, boulots...), vus pareil par tous.
    // L'auteur d'un PNJ (l'hote, sauf PNJ confie a un invite, voir plus bas) envoie, pour les PNJ actifs chez lui a
    // moins de 80 m d'un invite (10 fois par seconde a moins de 40 m, 5 au-dela), son heure, la pose du corps visible,
    // TOUTES ses couches d'animation (squelette, AnimationRoot, spine_upper... : clip dominant, second clip d'un fondu,
    // instant, poids ; une couche qui ne joue plus garde la pose de fin de son dernier clip, comme le telephone a
    // l'oreille) et les objets tenus ou poses (telephone, assiette, sonnerie...). Ce dernier clip est suivi pour tous
    // ses PNJ, meme loin des invites ou sans invite en partie : celui qui arrive recoit d'emblee la bonne pose tenue.
    // Chez les autres, les 8 derniers instantanes de chaque PNJ sont gardes et rejoues 0,25 s apres l'heure de son
    // auteur (une horloge par auteur) : pose interpolee entre les deux qui encadrent (extrapolee 0,3 s au plus si le
    // tampon se vide), posee directement, puis les couches appliquees parents d'abord.
    //  - Corps : tout maillage "bodymesh*" ; d'ordinaire sous un objet Char, mais aussi directement sous le PNJ
    //    (Teimo au bar Pivot/Teimo, Fleetari 'Neighbour 2', Fleawoman, Shitman, le vieux fou 'Man', le pianiste
    //    'Player', les ivrognes du bar, Teimo en luge...). Hors conducteurs de voitures (corps sous une CarDynamics).
    //  - Clients (sous .../Customers/) : tout l'objet du PNJ suit (racine, corps, collisions), sa logique
    //    locale est coupee (marche, telephone, regard) sauf ce qu'un joueur provoque (colere, coup, voiture :
    //    ca va a l'hote) ; et les memes clients sont la (presents ou absents comme chez l'hote).
    //  - Teimo en luge : la luge (TeimoInSled, menee par splineMove sur son trajet) suit aussi ; la renverser
    //    (HumanTrigger::CarHit "State 2" : ragdoll, ShopStatus ferme le magasin) est rejoue chez tous.
    //  - Personnel (automate Work : caissiere, serveurs) et autres PNJ : leur logique continue (la caissiere
    //    vient toujours servir l'invite), seul le corps visible suit ; leur telephone est coupe.
    // SERVICE ET BAGARRES : la logique d'un PNJ qui sert ou poursuit un joueur ne tourne que chez ce joueur (Jouni
    // apporte l'assiette a SA table, Virpi encaisse SA course, l'inspecteur controle SA voiture, le bagarreur du bar
    // le frappe LUI). Chaque client repere (Rules) le PNJ engage avec son joueur local (automate hors de ses etats de
    // repos, joueur a portee) ; un invite le demande a l'hote, qui le lui confie s'il n'est ni engage lui-meme ni deja
    // confie : la pose part alors de chez l'invite (relayee par l'hote), tous les autres la suivent, puis l'hote
    // reprend la main quand l'invite n'est plus engage. Les PNJ hostiles et les policiers a domicile (Whole) ne sont
    // montres aux autres que pendant l'engagement : chez eux toute leur logique est coupee (ils ne s'en prennent pas
    // a eux) et, absents (tires au sort, actives par le crime d'un autre), ils sont rendus visibles le temps du suivi.
    // Sans nouvelles de l'auteur depuis 2 s (PNJ loin de lui, eteint chez lui), chacun revoit les siens (retour en
    // 0,5 s). Hors circulation et passants (Traffic) et courses.
    public static class Npcs
    {
        const int MaxLayers = 4, MaxStates = 2, MaxProps = 32, Ring = 8;
        const float Delay = 0.25f, MaxExtrap = 0.3f, ReleaseAfter = 2f, BlendTime = 0.5f;
        // Msg.Npc : [U8 sorte] puis
        //  0 pose (auteur -> autres, non fiable ; d'un invite : a l'hote, qui relaie) : [U8 auteur][F32 heure de l'auteur]
        //    puis les PNJ (Write) ;
        //  1 demande (invite -> hote, fiable) : [I32 id][U8 1 prend | 0 rend] ;
        //  2 autorites (hote -> invites, fiable, a chaque changement et toutes les 5 s) : [U8 n] puis n x [I32 id][U8 joueur] ;
        //  3 renverse (tous, fiable, relaye par l'hote) : [U8 joueur][Str chemin::automate][Str etat].
        const int K_POSE = 0, K_CLAIM = 1, K_OWNERS = 2, K_EVENT = 3;

        // Une couche : une composante Animation du corps (ses etats releves une fois, empreinte de leur nom).
        class Layer
        {
            public Animation A; public AnimationState[] St; public int[] H; public bool[] Loop; public bool Built;
            public int Last;               // auteur : dernier clip dominant (tenu quand plus rien ne joue)
            public int Top = -1, Next = -1;  // auteur : les deux etats les plus forts au dernier releve
        }

        // Instantane de l'auteur : par couche, jusqu'a 2 etats (clip, instant, poids) aux indices l*MaxStates+k.
        class Snap
        {
            public float T; public bool HasRoot; public Vector3 Pos, RootPos; public Quaternion Rot, RootRot; public uint Props;
            public int Car = -1;   // Pos/Rot dans le repere de cette voiture (numero reseau) ; -1 : dans le monde
            public int Layers;
            public readonly int[] Count = new int[MaxLayers];
            public readonly bool[] Hold = new bool[MaxLayers];
            public readonly int[] Clip = new int[MaxLayers * MaxStates];
            public readonly float[] Nt = new float[MaxLayers * MaxStates], W = new float[MaxLayers * MaxStates];
        }

        // Engagement d'un PNJ avec le joueur local. Part : morceau du chemin du corps ; Fsm : automate du PNJ dont
        // l'etat dit l'engagement (null : actif et a portee suffit) ; Rest : ses etats de repos (engage hors de
        // ceux-ci) ; Range : distance joueur-corps ; Whole : hostile ou policier (montre seulement pendant
        // l'engagement, toute sa logique coupee chez les autres) ; Top : objet qui borne le PNJ (sa logique
        // d'apparition comprise), par son nom (vide : l'objet du PNJ).
        class Rule
        {
            public string Part, Fsm, Top = ""; public string[] Rest = new string[0]; public float Range; public bool Whole;
            public string Drives;   // seulement si le joueur local mene cette voiture (au volant, ou moteur laisse tournant)
        }

        // Etats lus dans le vidage de la scene (GAME-auto.txt).
        static readonly Rule[] Rules =
        {
            // Service : Virpi (MARKET/POST -> Move, Cashing...), Keijo (commande de burger), Jouni (ORDER -> plat a la
            // table), l'inspecteur (GLOBALEVENT -> controle), Fleetari (accueil, encaissement), agents des barrages.
            new Rule { Part = "/Virpi/", Fsm = "Work", Rest = new[] { "Delay", "Move random" }, Range = 12f },
            new Rule { Part = "/Keijo/", Fsm = "Work", Rest = new[] { "Delay", "Move 3", "Cashing 3", "Talk to Virpi", "Move 6", "State 2" }, Range = 12f },
            new Rule { Part = "/Jouni/", Fsm = "Work", Rest = new[] { "State 1" }, Range = 15f },
            new Rule { Part = "INSPECTION/LOD/Officer/", Fsm = "Animations", Rest = new[] { "Lean in", "State 1" }, Range = 25f },
            new Rule { Part = "INSPECTION/LOD/Officer/", Fsm = "AnimationsM", Rest = new[] { "Lean in", "State 5" }, Range = 25f },
            new Rule { Part = "Office/Fleetari/", Fsm = "Work", Rest = new[] { "Tobacco", "State 2", "Loop", "Wait", "State 1", "Wait player" }, Range = 10f },
            new Rule { Part = "TRAFFIC/Police/CopAlc", Fsm = "Animations", Rest = new[] { "Distance 1", "Anim down", "Distance 2", "Idle", "State 3" }, Range = 30f },
            // Hostiles : bagarreur du bar (active au hasard par FighterPub::Activate), bagarreur du dancing, Reijo le
            // concierge (active par Janitor::Logic quand on casse), le vieux fou a sa fenetre (Watcher::Logic).
            new Rule { Part = "FighterPub/Fighter2/", Fsm = "Move", Rest = new[] { "Wait player", "State 3" }, Range = 25f, Whole = true, Top = "FighterPub" },
            new Rule { Part = "FIGHTER/Fighter/", Fsm = "Move", Rest = new[] { "Standing", "Walk back", "State 1" }, Range = 25f, Whole = true, Top = "FIGHTER" },
            new Rule { Part = "Janitor/Reijo/", Range = 40f, Whole = true, Top = "Janitor" },
            new Rule { Part = "HouseRintamaCrazy/Watcher/Oldman/", Range = 40f, Whole = true, Top = "Watcher" },
            // Policiers a domicile (racine COPS, allumee chez le joueur recherche) : montres a qui est a 80 m.
            new Rule { Part = "COPS/", Range = 80f, Whole = true },
            // Client du taxi (TaxiWalker::Logic) : des que le chauffeur arrive (Distance 2), monte, roule, paie, descend, c'est
            // la machine du CHAUFFEUR qui le mene (son vehicule decide de la montee : PlayerCurrentVehicle == Taxi) -- pas
            // celle d'un passager ou d'un pieton a cote. Avant, tout le monde voyait la pose de l'hote : client reste sur le
            // trottoir quand un invite conduisait le taxi (retour d'un joueur, 08/10). Sa pose part dans le repere du taxi.
            new Rule { Part = "TAXIJOB/Customer1/", Fsm = "Logic", Rest = new[] { "Idle", "Check Jokke", "Reset customer", "Randomize loca", "ID", "Call", "State 11",
                "New location", "Activate", "Load", "Save?", "State 12", "Start walking", "Set mass 2", "Leave", "No pay", "State 13" }, Range = 30f,
                Drives = "MACHTWAGEN" },
        };

        // Logique qu'une voiture qui renverse le PNJ declenche (ragdoll, magasin ferme) : rejouee chez tous.
        static readonly string[] HitRoots = { "STORE_AREA/TeimoInSled" };
        static readonly string[] HitStates = { "State 2", "Accident" };

        class Npc
        {
            public int Id; public string Path; public Transform Char, Root, Customer, Moved, Top; public bool Ambient, Whole;
            public Layer[] Layers; public GameObject[] Props;
            public Vector3 RestPos; public Quaternion RestRot;
            public bool InRange, Near;                                     // auteur : distance aux autres
            public Snap[] Buf; public int Head, Count, Sender;              // suiveur : anneau trie par heure de l'auteur
            public bool Held, Dry, Shown; public float LastRecv; public int Extrap;
            public List<PlayMakerFSM> Muted; public List<Rigidbody> Frozen;
            public float BlendUntil; public Vector3 BlendPos; public Quaternion BlendRot;
            public Rule[] Rules; public PlayMakerFSM[] RuleFsms;
            public bool Engaged; public float EngagedAt, ClaimAt;
            public Vector3 HoldAt; public float HeldSince; public List<Transform> Phones;   // voix (Voices)
        }
        static readonly Dictionary<int, Npc> byId = new Dictionary<int, Npc>();
        static readonly List<Npc> all = new List<Npc>();   // les memes, parcourus a chaque image sans allocation
        static readonly List<Vector3> guests = new List<Vector3>();
        static readonly Snap scratch = new Snap();
        // PNJ confies a un invite (id -> numero du joueur) ; absent : l'hote. Tenu par l'hote, recopie chez tous.
        static readonly Dictionary<int, int> owners = new Dictionary<int, int>();
        static float nextScan = -1, nextSend, lastLate, nextEngage, nextOwners, hitAt = -1, nextHit;
        static int sent, recv, tick, held, released, switched, claims;
        static bool anyHeld, anyBlend, subscribed;
        static Transform me;
        static readonly List<PlayMakerFSM> hitPending = new List<PlayMakerFSM>();
        static int hitHooked, hitSent, hitRecv;

        public static void OnLevelLoaded()
        {
            byId.Clear(); all.Clear(); owners.Clear(); clocks.Clear(); hitPending.Clear();
            RestoreShown(); shown.Clear(); shownOrig.Clear();
            float now = Time.realtimeSinceStartup;
            nextScan = PlayerSync.InGame ? now + 17f : -1;
            // Avant le releve du monde (16 s) : les automates CarHit rejoues par ce module sont a lui.
            hitAt = PlayerSync.InGame ? now + 9f : -1;
            anyHeld = anyBlend = false; me = null; hitHooked = 0;
            watch = null; testStep = 0; testNext = lastShot = 0f; fluHas = false;
        }

        // Identifiant stable : empreinte du chemin du corps (meme scene -> meme nombre partout).
        static int Hash(string s)
        {
            unchecked { uint h = 2166136261; foreach (char c in s) { h ^= c; h *= 16777619; } return (int)h; }
        }

        static int Owner(Npc n) { int o; return owners.TryGetValue(n.Id, out o) ? o : 0; }

        // Chemin qui identifie le corps. Agents des barrages : sans le lieu du jour (Police::Parenting les accroche sous
        // Ratsia1Airport, Ratsia2Skihill ou Ratsia3Fields), "TRAFFIC/Police/CopAlc1/Pivot/Char".
        static string KeyPath(Transform ch)
        {
            string path = Recon.Path(ch);
            if (!path.StartsWith("TRAFFIC/Police/")) return path;
            for (Transform t = ch; t != null && t.parent != null; t = t.parent)
                if (t.name.StartsWith("CopAlc") || t.name.StartsWith("CopRadar"))
                    return "TRAFFIC/Police/" + path.Substring(Recon.Path(t.parent).Length + 1);
            return path;
        }

        // Corps ecartes : joueur, interface, courses, voitures de PNJ et circulation (Traffic, sauf les agents des
        // barrages), passants (Traffic), prison (personnelle), modeles inactifs (sauf COPS : allumee seulement chez
        // le joueur recherche, les autres doivent connaitre ses agents pour les montrer).
        static bool SkipBody(Transform ch, string path, bool underChar)
        {
            Transform rt = ch.root;
            string root = rt.name;
            if (root.StartsWith("MWCoop") || root == "PLAYER" || root == "GUI" || root == "RACES" || root == "NPC_CARS" || root == "JAIL") return true;
            if (root == "TRAFFIC" && !path.StartsWith("TRAFFIC/Police/Cop")) return true;
            if (root == "HUMANS" && path.StartsWith("HUMANS/Randomizer")) return true;
            if (!rt.gameObject.activeInHierarchy && !(root == "COPS" && Game.FindAny("COPS") == rt.gameObject)) return true;
            if (!underChar)
                for (Transform t = ch; t != null; t = t.parent) if (t.GetComponent("CarDynamics") != null) return true;   // conducteurs
            return false;
        }

        static void Scan()
        {
            int before = byId.Count, clients = 0, bodies = 0, ruled = 0;
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(SkinnedMeshRenderer)))
            {
                var smr = (SkinnedMeshRenderer)o;
                if (smr.hideFlags != HideFlags.None || !smr.name.StartsWith("bodymesh")) continue;
                Transform ch = smr.transform.parent;
                if (ch == null || ch.parent == null || ch.name == "RagDoll") continue;
                bool underChar = ch.name == "Char";
                string path = KeyPath(ch);
                if (SkipBody(ch, path, underChar)) continue;
                int id = Hash(path);
                if (byId.ContainsKey(id)) continue;
                var n = new Npc { Id = id, Path = path, Char = ch, RestPos = ch.localPosition, RestRot = ch.localRotation };
                // Objet du PNJ : au-dessus de Pivot (et de PhysicsPivot) ; client : l'enfant de Customers.
                Transform piv = ch.parent;
                n.Root = piv != null && piv.name == "Pivot" ? piv.parent : piv;
                if (n.Root != null && n.Root.name == "PhysicsPivot" && n.Root.parent != null) n.Root = n.Root.parent;
                for (Transform t = ch; t.parent != null; t = t.parent) if (t.parent.name == "Customers") { n.Customer = t; break; }
                n.Ambient = n.Customer != null && n.Root != null && Game.FsmOn(n.Root.gameObject, "Work") == null;
                if (n.Ambient) { clients++; n.Moved = n.Root; }
                else for (Transform t = ch.parent; t != null; t = t.parent) if (t.GetComponent("splineMove") != null) { n.Moved = t; break; }   // luge
                if (!underChar) bodies++;
                // Couches : toutes les animations du corps, dans l'ordre de la hierarchie (parents d'abord). Corps hors
                // Char : aussi celle de son parent, qui l'anime (TeimoInBar/Pivot : trajets magasin-bar ; Oldman).
                var layers = new List<Layer>();
                Animation pa = underChar ? null : ch.parent.GetComponent<Animation>();
                if (pa != null) layers.Add(new Layer { A = pa });
                foreach (Animation a in ch.GetComponentsInChildren<Animation>(true)) if (layers.Count < MaxLayers) layers.Add(new Layer { A = a });
                n.Layers = layers.ToArray();
                // Objets tenus : sous le corps ; pour un client, sous tout son objet (plateau, telephone sur la
                // table, sonnerie).
                var props = new List<GameObject>();
                foreach (Transform t in (n.Ambient ? n.Root : ch).GetComponentsInChildren<Transform>(true))
                    if (props.Count < MaxProps && (t.GetComponent<MeshRenderer>() != null || n.Ambient && t.GetComponent<AudioSource>() != null)) props.Add(t.gameObject);
                n.Props = props.ToArray();
                if (AttachRules(n)) ruled++;
                byId[id] = n; all.Add(n);
            }
            if (byId.Count != before)
                Log.Info("PNJ : " + byId.Count + " suivis (" + clients + " clients, " + bodies + " corps hors Char, " + ruled + " engageables de plus)");
        }

        // Regles du PNJ et leurs automates (sur son objet ou dessous).
        static bool AttachRules(Npc n)
        {
            List<Rule> rl = null; List<PlayMakerFSM> rf = null;
            string top = "";
            foreach (Rule r in Rules)
            {
                if (!n.Path.Contains(r.Part)) continue;
                if (rl == null) { rl = new List<Rule>(); rf = new List<PlayMakerFSM>(); }
                rl.Add(r);
                rf.Add(r.Fsm != null ? FindFsm(n, r.Fsm) : null);
                if (r.Whole) { n.Whole = true; top = r.Top; }
            }
            if (rl == null) return false;
            n.Rules = rl.ToArray(); n.RuleFsms = rf.ToArray();
            if (n.Whole)
            {
                for (Transform t = n.Char; t != null && top.Length > 0; t = t.parent) if (t.name == top) { n.Top = t; break; }
                if (n.Top == null) n.Top = n.Root != null ? n.Root : n.Char;
            }
            return true;
        }

        static PlayMakerFSM FindFsm(Npc n, string name)
        {
            Transform scope = n.Root != null ? n.Root : n.Char;
            foreach (PlayMakerFSM f in scope.GetComponentsInChildren<PlayMakerFSM>(true)) if (f.FsmName == name) return f;
            return null;
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

        // ---------------------------------------------------------------- envoi
        public static void Update()
        {
            if (!Session.Active || nextScan < 0 || !PlayerSync.InGame) return;
            if (!subscribed) { subscribed = true; Session.PlayerLeft += OnPlayerLeft; }
            float now = Time.realtimeSinceStartup;
            if (hitAt > 0 && now >= hitAt) { hitAt = -1; ClaimHits(); }
            if (hitPending.Count > 0 && now >= nextHit) { nextHit = now + 1f; InjectHits(); }
            if (now >= nextScan) { nextScan = now + 45f; Scan(); }
            if (now >= nextEngage) { nextEngage = now + 0.2f; Engage(now); }
            if (Session.IsHost && (owners.Count > 0 || HostEngaged()) && now >= nextOwners) { nextOwners = now + 5f; SendOwners(); }   // (invite arrive, liste perdue)
            if (now < nextSend) return;
            nextSend = now + 0.1f;
            if (Session.IsHost) SendHost(now); else SendGuest(now);
        }

        static NetWriter Header(float now) { return new NetWriter(Msg.Npc).U8(K_POSE).U8(Session.LocalId).F32(now); }

        static void SendHost(float now)
        {
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
                if (n.Char == null || Owner(n) != 0) continue;     // confie a un invite : c'est lui qui l'envoie
                if (n.Whole && !n.Engaged) continue;             // hostile, policier : seulement pendant qu'il s'en prend a l'hote
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
                int len = present ? 38 + (n.Moved != null ? 28 : 0) + n.Layers.Length * (1 + MaxStates * 9) : 5;
                if (w != null && w.Length + len > 1000) { Session.SendAll(w, false); w = null; }
                if (w == null) w = Header(now);
                if (absent) { w.I32(n.Id).U8(0); continue; }
                Write(w, n);
                sent++;
            }
            if (w != null) Session.SendAll(w, false);
        }

        // Invite : les PNJ qui lui sont confies (peu nombreux), a l'hote qui relaie.
        static void SendGuest(float now)
        {
            if (owners.Count == 0) return;
            NetWriter w = null;
            for (int k = 0; k < all.Count; k++)
            {
                Npc n = all[k];
                if (n.Char == null || Owner(n) != Session.LocalId || !n.Char.gameObject.activeInHierarchy) continue;
                Track(n);
                int len = 38 + (n.Moved != null ? 28 : 0) + n.Layers.Length * (1 + MaxStates * 9);
                if (w != null && w.Length + len > 1000) { Session.SendToHost(w, false); w = null; }
                if (w == null) w = Header(now);
                Write(w, n);
                sent++;
            }
            if (w != null) Session.SendToHost(w, false);
        }

        // [I32 id][U8 1 present | 2 objet deplace joint][Vec Quat corps]([Vec Quat objet deplace])[I32 objets tenus]
        // [U8 couches] puis par couche [U8 etats | 0x80 tenu] et par etat [I32 clip][F32 instant][U8 poids].
        // PNJ assis dans une voiture (client du taxi, range sous GetInPivotTaxi) : pose dans le repere de la voiture,
        // posee chez les autres sur LEUR voiture (copie du taxi d'un autre conducteur) -- en coordonnees du monde, il
        // trainait derriere la copie qui avance (pose rejouee 0,25 s apres) ; retour d'un joueur, 08/10.
        static void Write(NetWriter w, Npc n)
        {
            bool root = n.Moved != null;
            Transform car = VehicleSync.CarRoot(n.Char);
            int ci = car != null ? VehicleSync.CarIndex(car.GetComponent<Rigidbody>()) : -1;
            if (ci > 255) ci = -1;
            w.I32(n.Id).U8((root ? 3 : 1) | (ci >= 0 ? 4 : 0));
            if (ci >= 0) w.Vec(car.InverseTransformPoint(n.Char.position)).Quat(Quaternion.Inverse(car.rotation) * n.Char.rotation).U8(ci);
            else w.Vec(n.Char.position).Quat(n.Char.rotation);
            if (root) w.Vec(n.Moved.position).Quat(n.Moved.rotation);
            uint bits = 0;
            for (int i = 0; i < n.Props.Length; i++) if (n.Props[i] != null && n.Props[i].activeSelf) bits |= 1u << i;
            w.I32((int)bits).U8(n.Layers.Length);
            for (int l = 0; l < n.Layers.Length; l++) WriteLayer(w, n.Layers[l]);
        }

        // Auteur, a chaque tour : etats dominants de chaque couche et dernier clip joue. PNJ ou couche eteint : rien
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

        // ---------------------------------------------------------------- engagement et autorite
        static Transform MePlayer()
        {
            if (me == null) { GameObject p = GameObject.Find("PLAYER"); if (p != null) me = p.transform; }
            return me;
        }

        // Toutes les 0,2 s : PNJ engages avec le joueur local. Un PNJ hostile suivi (logique coupee ici) ne l'est
        // jamais. Fin d'engagement retenue 2 s (un etat de repos traverse en passant ne rend pas la main).
        static void Engage(float now)
        {
            Transform p = MePlayer();
            if (p == null) return;
            Vector3 mp = p.position;
            for (int k = 0; k < all.Count; k++)
            {
                Npc n = all[k];
                if (n.Rules == null || n.Char == null) continue;
                bool e = !(n.Whole && n.Held) && Check(n, mp);
                if (e) n.EngagedAt = now;
                bool on = e || n.Engaged && now - n.EngagedAt < 2f;
                if (on != n.Engaged)
                {
                    n.Engaged = on;
                    if (++claims <= 60) Log.Info("PNJ : " + Name(n) + (on ? " engage avec le joueur local (" + EngageWhy(n) + ")" : " n'est plus engage ici"));
                    if (!Session.IsHost) SendClaim(n, on, now);
                    else SendOwners();   // (l'hote annonce aussi les siens : Jobs, LedElsewhere)
                }
                else if (on && !Session.IsHost && Owner(n) != Session.LocalId && now >= n.ClaimAt) SendClaim(n, true, now);   // refus, perte : redemande
            }
        }

        static bool Check(Npc n, Vector3 mp)
        {
            if (!n.Char.gameObject.activeInHierarchy) return false;
            // Ou la logique d'ici le met (corps suivi : sa place de repos sous son parent, pas la pose de l'autre).
            Vector3 at = n.Held && n.Char.parent != null ? n.Char.parent.TransformPoint(n.RestPos) : n.Char.position;
            for (int i = 0; i < n.Rules.Length; i++)
            {
                Rule r = n.Rules[i];
                if ((at - mp).sqrMagnitude > r.Range * r.Range) continue;
                if (r.Drives != null) { Transform lead = VehicleSync.LocalLeadRoot; if (lead == null || lead.name != r.Drives) continue; }
                if (r.Fsm == null) return true;
                PlayMakerFSM f = n.RuleFsms[i];
                if (f == null || !f.enabled || !f.gameObject.activeInHierarchy) continue;
                string s = f.ActiveStateName;
                if (string.IsNullOrEmpty(s) || System.Array.IndexOf(r.Rest, s) >= 0) continue;
                return true;
            }
            return false;
        }

        static string EngageWhy(Npc n)
        {
            for (int i = 0; i < n.Rules.Length; i++)
            {
                PlayMakerFSM f = n.RuleFsms[i];
                if (n.Rules[i].Fsm == null) return "present a " + n.Rules[i].Range + " m";
                if (f != null) return f.FsmName + " " + f.ActiveStateName;
            }
            return "?";
        }

        static void SendClaim(Npc n, bool take, float now)
        {
            n.ClaimAt = now + 3f;
            Session.SendToHost(new NetWriter(Msg.Npc).U8(K_CLAIM).I32(n.Id).U8(take ? 1 : 0), true);
        }

        // Hote : un invite demande (ou rend) un PNJ engage avec son joueur.
        static void OnClaim(Peer from, NetReader r)
        {
            if (!Session.IsHost) return;
            int id = r.I32();
            bool take = r.U8() != 0;
            Npc n;
            byId.TryGetValue(id, out n);
            string name = n != null ? Name(n) : id.ToString("x8");
            int cur;
            bool has = owners.TryGetValue(id, out cur);
            if (take)
            {
                if (has && cur == from.Id) { SendOwners(); return; }
                if (has) { if (++claims <= 60) Log.Info("PNJ : " + name + " demande par #" + from.Id + ", deja confie a #" + cur); return; }
                if (n != null && n.Engaged) { if (++claims <= 60) Log.Info("PNJ : " + name + " demande par #" + from.Id + ", refuse (engage avec l'hote)"); return; }
                owners[id] = from.Id;
                Log.Info("PNJ : " + name + " confie a #" + from.Id + " (sa pose part de chez lui)");
                SendOwners();
            }
            else if (has && cur == from.Id)
            {
                owners.Remove(id);
                Log.Info("PNJ : " + name + " rendu par #" + from.Id + ", l'hote reprend la main");
                SendOwners();
            }
        }

        // [U8 nombre] puis [I32 id][U8 joueur] : liste complete (absente : l'hote). Joueur 0 : engage avec l'hote (un
        // invite ne peut alors pas le prendre ; sa logique d'ici ne fait pas autorite, voir LedElsewhere).
        static void SendOwners()
        {
            var list = new List<KeyValuePair<int, int>>(owners);
            foreach (Npc x in all) if (x.Rules != null && x.Engaged && !owners.ContainsKey(x.Id)) list.Add(new KeyValuePair<int, int>(x.Id, 0));
            var w = new NetWriter(Msg.Npc).U8(K_OWNERS);
            int n = Mathf.Min(list.Count, 150);
            w.U8(n);
            for (int i = 0; i < n; i++) w.I32(list[i].Key).U8(list[i].Value);
            Session.Broadcast(w, true);
        }

        static bool HostEngaged()
        {
            foreach (Npc x in all) if (x.Rules != null && x.Engaged) return true;
            return false;
        }

        // Jobs : automate (ou ce qui est dessous) d'un PNJ engage qu'un AUTRE joueur mene en ce moment (confie a un invite,
        // ou engage avec l'hote) : sa logique d'ici ne fait pas autorite, ses transitions ne partent pas. Client du taxi :
        // l'hote passager voyait son client a 4 m et le faisait repartir en "Distance 2" chez le chauffeur, en pleine course.
        public static bool LedElsewhere(Transform t)
        {
            if (owners.Count == 0 || t == null) return false;
            for (int k = 0; k < all.Count; k++)
            {
                Npc n = all[k];
                if (n.Rules == null || n.Root == null || (t != n.Root && !t.IsChildOf(n.Root))) continue;
                int o;
                return owners.TryGetValue(n.Id, out o) && o != Session.LocalId;
            }
            return false;
        }

        static void OnOwners(NetReader r)
        {
            if (Session.IsHost) return;
            int count = r.U8();
            var fresh = new Dictionary<int, int>();
            for (int i = 0; i < count; i++) { int id = r.I32(); fresh[id] = r.U8(); }
            foreach (KeyValuePair<int, int> kv in fresh)
            {
                int old;
                if (owners.TryGetValue(kv.Key, out old) && old == kv.Value) continue;
                Npc n;
                if (byId.TryGetValue(kv.Key, out n)) Log.Info("PNJ : " + Name(n) + " mene par #" + kv.Value + (kv.Value == Session.LocalId ? " (moi : sa pose part d'ici)" : ""));
            }
            foreach (KeyValuePair<int, int> kv in owners)
            {
                Npc n;
                if (!fresh.ContainsKey(kv.Key) && byId.TryGetValue(kv.Key, out n)) Log.Info("PNJ : " + Name(n) + " de nouveau mene par l'hote");
            }
            owners.Clear();
            foreach (KeyValuePair<int, int> kv in fresh) owners[kv.Key] = kv.Value;
        }

        // Joueur parti : ses PNJ reviennent a l'hote.
        static void OnPlayerLeft(PlayerInfo pi)
        {
            if (!Session.IsHost) { owners.Clear(); return; }
            var gone = new List<int>();
            foreach (KeyValuePair<int, int> kv in owners) if (kv.Value == pi.Id) gone.Add(kv.Key);
            if (gone.Count == 0) return;
            foreach (int id in gone) owners.Remove(id);
            Log.Info("PNJ : " + gone.Count + " PNJ de #" + pi.Id + " repris par l'hote (parti)");
            SendOwners();
        }

        // ---------------------------------------------------------------- renverse par une voiture
        class HitHook : ModHook
        {
            public override string Module { get { return "PNJ"; } }
            public string Key, Target;
            public override void OnEnter()
            {
                try
                {
                    if (Replay.Depth == 0 && Session.Active && Session.RemoteCount > 0)
                    {
                        hitSent++;
                        MWCoop.Log.Info("PNJ : " + Key + " -> " + Target + " ici, envoye aux autres");
                        Session.SendAll(new NetWriter(Msg.Npc).U8(K_EVENT).U8(Session.LocalId).Str(Key).Str(Target), true);
                    }
                }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        static void ClaimHits()
        {
            foreach (string path in HitRoots)
            {
                GameObject g = Game.FindAny(path);
                if (g == null) { Log.Warn("PNJ : " + path + " introuvable"); continue; }
                foreach (PlayMakerFSM f in g.GetComponentsInChildren<PlayMakerFSM>(true))
                {
                    if (f.FsmName != "CarHit") continue;
                    if (Replay.Claim(f, "PNJ")) hitPending.Add(f);
                    else Log.Warn("PNJ : " + Recon.Path(f.transform) + "::CarHit deja pris par " + Replay.Owner(f));
                }
            }
        }

        static bool Ready(PlayMakerFSM f)
        {
            if (f == null || !f.gameObject.activeInHierarchy) return false;
            foreach (FsmState s in f.Fsm.States) if (!s.IsInitialized) return false;
            return true;
        }

        static void InjectHits()
        {
            for (int i = hitPending.Count - 1; i >= 0; i--)
            {
                PlayMakerFSM f = hitPending[i];
                if (f == null) { hitPending.RemoveAt(i); continue; }
                if (!Ready(f)) continue;
                hitPending.RemoveAt(i);
                string key = Recon.Path(f.transform) + "::" + f.FsmName;
                foreach (string s in HitStates)
                {
                    FsmState st = f.Fsm.GetState(s);
                    if (st == null) continue;
                    var list = new List<FsmStateAction>(st.Actions);
                    list.Insert(0, new HitHook { Key = key, Target = s });
                    st.Actions = list.ToArray();
                    hitHooked++;
                    Log.Info("PNJ : " + key + " \"" + s + "\" (renverse) rejoue chez tous");
                }
            }
        }

        static void OnEvent(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            string key = r.Str(), state = r.Str();
            if (Session.IsHost) Session.Broadcast(new NetWriter(Msg.Npc).U8(K_EVENT).U8(who).Str(key).Str(state), true, who);
            int sep = key.LastIndexOf("::");
            GameObject g = sep > 0 ? Game.FindAny(key.Substring(0, sep)) : null;
            PlayMakerFSM f = g != null ? Game.FsmOn(g, key.Substring(sep + 2)) : null;
            if (f == null) { Log.Warn("PNJ : " + key + " introuvable ici"); return; }
            hitRecv++;
            Replay.Depth++;
            try { Game.SetState(f, state); }
            finally { Replay.Depth--; }
            Log.Info("PNJ : " + key + " -> " + state + " rejoue (venu de #" + who + ")");
        }

        // ---------------------------------------------------------------- suiveur
        // Decalage d'horloge par auteur (heure locale - heure de l'auteur) : minimum des 5 a 10 dernieres secondes
        // (le paquet le moins retarde), rejoint doucement (50 ms par seconde) pour que le rejeu ne saute jamais.
        class Clk { public float Min = float.MaxValue, Prev = float.MaxValue, Window, Target, Off; public bool Set; }
        static readonly Dictionary<int, Clk> clocks = new Dictionary<int, Clk>();

        static Clk ClockOf(int sender)
        {
            Clk c;
            if (!clocks.TryGetValue(sender, out c)) { c = new Clk(); clocks[sender] = c; }
            return c;
        }

        static void Clock(Clk c, float now, float t)
        {
            float d = now - t;
            if (now >= c.Window) { c.Prev = c.Min; c.Min = float.MaxValue; c.Window = now + 5f; }
            if (d < c.Min) c.Min = d;
            c.Target = Mathf.Min(c.Min, c.Prev);
            if (!c.Set || Mathf.Abs(c.Target - c.Off) > 0.5f) { c.Off = c.Target; c.Set = true; }
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int kind = r.U8();
            switch (kind)
            {
                case K_POSE: OnPose(from, r); break;
                case K_CLAIM: OnClaim(from, r); break;
                case K_OWNERS: OnOwners(r); break;
                case K_EVENT: OnEvent(from, r); break;
            }
        }

        static void OnPose(Peer from, NetReader r)
        {
            int sender = r.U8();
            if (Session.IsHost)
            {
                // Pose d'un PNJ confie a cet invite : relayee aux autres invites, appliquee ici.
                sender = from.Id;
                Session.Broadcast(new NetWriter(Msg.Npc).U8(K_POSE).U8(sender).Raw(r.Rest()), false, sender);
            }
            float now = Time.realtimeSinceStartup, t = r.F32();
            Clock(ClockOf(sender), now, t);
            while (r.More)
            {
                int id = r.I32(), flags = r.U8();
                Npc n;
                if (!byId.TryGetValue(id, out n) || n.Char == null) n = null;
                if (n != null && Owner(n) != sender) n = null;      // pas (ou plus) son auteur : paquet en retard
                if ((flags & 1) == 0) { if (n != null && sender == 0) Absent(n); continue; }
                if (n != null && n.Sender != sender) { n.Sender = sender; n.Count = 0; }
                Snap s = n != null ? Slot(n, t) : null;
                Read(r, s ?? scratch, flags);
                if (s == null) continue;
                Commit(n);
                recv++;
                n.LastRecv = now;
                if (n.Whole && !n.Char.gameObject.activeInHierarchy && NearMe(n)) Show(n);
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
                if (t <= last) n.Count = 0;   // l'auteur a recommence (nouvelle partie) : on repart de zero
            }
            Snap s = n.Buf[(n.Head + n.Count) % Ring];
            s.T = t;
            return s;
        }

        static void Commit(Npc n) { if (n.Count < Ring) n.Count++; else n.Head = (n.Head + 1) % Ring; }

        static void Read(NetReader r, Snap s, int flags)
        {
            s.Pos = r.Vec(); s.Rot = r.Quat();
            s.Car = (flags & 4) != 0 ? r.U8() : -1;
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
            Transform p = MePlayer();
            return p != null && (p.position - n.Char.position).sqrMagnitude < 100f * 100f;
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
            n.HoldAt = n.Char.position; n.HeldSince = Time.realtimeSinceStartup;   // place d'ici, avant la premiere pose recue
            Mute(n);
            if (++held <= 20) Log.Info("PNJ : " + Name(n) + " suit #" + n.Sender + (n.Muted.Count > 0 ? " (" + n.Muted.Count + " automates coupes)" : ""));
        }

        // Client : toute sa logique locale coupee (sauf ce qu'un joueur provoque) et ses corps rigides figes, le
        // temps que l'hote le mene ; hostile ou policier : toute sa logique (il ne s'en prend pas au joueur d'ici) ;
        // les autres : seulement leur telephone (sa sonnerie et ses appels viennent de l'hote).
        static void Mute(Npc n)
        {
            if (n.Muted != null) return;
            n.Muted = new List<PlayMakerFSM>(); n.Frozen = new List<Rigidbody>();
            bool whole = n.Whole || n.Ambient && n.Root != null;
            Transform scope = n.Whole ? n.Top : whole ? n.Root : n.Char;
            foreach (PlayMakerFSM f in scope.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                if (!f.enabled) continue;
                if (!n.Whole && (whole ? WorldFsms.PlayerCaused(f) : !(f.FsmName == "Logic" && f.gameObject.name.StartsWith("Phone")))) continue;
                f.enabled = false; n.Muted.Add(f);
            }
            if (!whole) return;
            foreach (Rigidbody rb in scope.GetComponentsInChildren<Rigidbody>(true))
                if (!rb.isKinematic && !InRagdoll(rb.transform, scope)) { rb.isKinematic = true; n.Frozen.Add(rb); }
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
            if (n.Shown) Unshow(n);
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

        // ---------------------------------------------------------------- PNJ montres le temps du suivi
        // Hostile ou policier absent ici (bagarreur tire au sort chez l'autre, agents venus pour l'autre) : la chaine
        // de ses parents est allumee ; au-dessus de son objet (Top), les autres enfants des objets allumes par nous
        // sont eteints (seul ce PNJ apparait, pas tous les agents de COPS) et les automates de ces objets coupes.
        // L'etat d'origine de tout objet touche est note : a chaque changement, tout est remis puis refait pour les
        // PNJ encore montres.
        static readonly List<Npc> shown = new List<Npc>();
        static readonly Dictionary<GameObject, bool> shownOrig = new Dictionary<GameObject, bool>();
        static readonly List<PlayMakerFSM> shownMuted = new List<PlayMakerFSM>();

        static void Show(Npc n)
        {
            if (n.Shown) return;
            n.Shown = true; shown.Add(n);
            ReShow();
            if (++switched <= 40) Log.Info("PNJ : " + Name(n) + " montre (absent ici, mene par #" + n.Sender + ")");
        }

        static void Unshow(Npc n)
        {
            n.Shown = false; shown.Remove(n);
            ReShow();
        }

        static void Touch(GameObject g) { if (!shownOrig.ContainsKey(g)) shownOrig[g] = g.activeSelf; }

        static void RestoreShown()
        {
            foreach (PlayMakerFSM f in shownMuted) if (f != null) f.enabled = true;
            shownMuted.Clear();
            foreach (KeyValuePair<GameObject, bool> kv in shownOrig) if (kv.Key != null && kv.Key.activeSelf != kv.Value) kv.Key.SetActive(kv.Value);
        }

        static void ReShow()
        {
            RestoreShown();
            if (shown.Count == 0) { shownOrig.Clear(); return; }
            var needed = new HashSet<Transform>();
            foreach (Npc n in shown) for (Transform t = n.Char; t != null; t = t.parent) needed.Add(t);
            foreach (Npc n in shown)
            {
                if (n.Char == null) continue;
                var chain = new List<Transform>();
                bool above = false;
                for (Transform t = n.Char; t != null; t = t.parent) chain.Add(t);
                for (int i = chain.Count - 1; i >= 0; i--)
                {
                    Transform t = chain[i];
                    above = t != n.Top && IsAbove(t, n.Top);
                    if (t.gameObject.activeSelf) continue;
                    Touch(t.gameObject);
                    if (above)
                    {
                        foreach (Transform c in t)
                            if (c.gameObject.activeSelf && !needed.Contains(c)) { Touch(c.gameObject); c.gameObject.SetActive(false); }
                        foreach (PlayMakerFSM f in t.GetComponents<PlayMakerFSM>()) if (f.enabled) { f.enabled = false; shownMuted.Add(f); }
                    }
                    t.gameObject.SetActive(true);
                }
            }
        }

        static bool IsAbove(Transform t, Transform top)
        {
            for (Transform p = top != null ? top.parent : null; p != null; p = p.parent) if (p == t) return true;
            return false;
        }

        static float nextErrorLog;

        // Apres l'animation du jeu : pose, couches et objets de l'auteur, a l'heure de rejeu.
        public static void LateUpdate()
        {
            if (all.Count == 0 || !anyHeld && !anyBlend) return;
            bool session = Session.Active;
            float now = Time.realtimeSinceStartup, dt = Mathf.Clamp(now - lastLate, 0f, 0.5f);
            lastLate = now;
            foreach (Clk c in clocks.Values) if (c.Set) c.Off = Mathf.MoveTowards(c.Off, c.Target, 0.05f * dt);
            anyHeld = anyBlend = false;
            for (int k = 0; k < all.Count; k++)
            {
                Npc n = all[k];
                if (n.Char == null) continue;
                try
                {
                    if (n.Held && (!session || Owner(n) == Session.LocalId || now - n.LastRecv > ReleaseAfter || n.Count == 0 || !n.Char.gameObject.activeInHierarchy)) Release(n, now);
                    if (n.BlendUntil > 0f) { BlendBack(n, now); anyBlend = true; }
                    if (!n.Held) continue;
                    anyHeld = true;
                    Apply(n, now - ClockOf(n.Sender).Off - Delay);
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
                if (span > 0.01f && span < 1f && last.Car == p.Car && (last.Pos - p.Pos).sqrMagnitude < 25f)
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
                // Grand saut chez l'auteur (teleporte), ou monte / descendu de voiture (autre repere) : pas de glissade
                // a travers les murs.
                if (b != null && (b.Car != a.Car || (b.Pos - a.Pos).sqrMagnitude > 25f)) { if (u >= 0.5f) a = b; b = null; u = 0f; }
            }
            Vector3 pos; Quaternion rot;
            if (b != null) { pos = Vector3.Lerp(a.Pos, b.Pos, u); rot = Quaternion.Slerp(a.Rot, b.Rot, u); }
            else { pos = a.Pos + vel * ex; rot = a.Rot; }
            bool place = true;
            if (a.Car >= 0)
            {
                Rigidbody cb = VehicleSync.CarBody(a.Car);
                if (cb != null) { pos = cb.transform.TransformPoint(pos); rot = cb.transform.rotation * rot; }
                else place = false;   // voiture inconnue ici : le corps reste ou il est
            }
            // Objet deplace avec le corps (client : tout son objet ; Teimo : la luge), avant le corps qui est dessous.
            if (n.Moved != null && a.HasRoot)
            {
                if (b != null && b.HasRoot) { n.Moved.position = Vector3.Lerp(a.RootPos, b.RootPos, u); n.Moved.rotation = Quaternion.Slerp(a.RootRot, b.RootRot, u); }
                else { n.Moved.position = a.RootPos + rvel * ex; n.Moved.rotation = a.RootRot; }
            }
            // Couches : temps de l'auteur a l'instant rejoue (extrapole au plus de 0,3 s). Avant la pose du corps :
            // celle du parent d'un corps hors Char deplace le corps.
            float at = b != null ? rt : a.T + ex;
            for (int l = 0; l < n.Layers.Length; l++) ApplyLayer(n.Layers[l], l, a, b, u, at);
            if (place) { n.Char.position = pos; n.Char.rotation = rot; }
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

        // Etats de l'auteur actives avec leur poids et leur instant, les autres eteints, puis la couche echantillonnee.
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

        // ---------------------------------------------------------------- voix des PNJ suivis (Voices)
        // Un PNJ suivi ici dont la logique est coupee (client, hostile, policier : tout son objet ; personnel : son
        // telephone) ne parle que par la voix de son auteur : ce qu'il dirait encore ici (appel en cours au moment du
        // suivi, replique relancee par un automate laisse tourner comme Anger) est coupe, et la replique recue est
        // accrochee a lui. 'follow' : objet que la variante MasterAudio suit (null : on juge a la position). Rend
        // l'objet ou poser la copie (corps ou telephone), null si ce n'est pas un tel PNJ.
        public static Transform MutedSpeaker(Transform follow, Vector3 pos)
        {
            if (!anyHeld) return null;
            float now = Time.realtimeSinceStartup;
            for (int k = 0; k < all.Count; k++)
            {
                Npc n = all[k];
                if (!n.Held || n.Muted == null || n.Char == null) continue;
                if (n.Whole || n.Ambient)
                {
                    Transform scope = n.Whole ? n.Top : n.Root;
                    if (scope == null) scope = n.Char;
                    if (follow != null ? follow.IsChildOf(scope)
                        : (n.Char.position - pos).sqrMagnitude < 2.6f || now - n.HeldSince < 3f && (n.HoldAt - pos).sqrMagnitude < 2.6f)
                        return n.Char;
                    continue;
                }
                foreach (PlayMakerFSM f in n.Muted)
                    if (f != null && (follow != null ? follow.IsChildOf(f.transform) : (f.transform.position - pos).sqrMagnitude < 0.7f)) return f.transform;
            }
            return null;
        }

        // Auteur : la replique vient-elle d'un PNJ mene d'ici que les autres suivent logique coupee (meme
        // perimetre) ? Voices l'envoie alors aussi quand c'est un autre joueur qui est pres de lui. Hote : seulement
        // les PNJ envoyes aux invites (a moins de 80 m de l'un d'eux) ; un hostile, seulement pendant l'engagement.
        public static bool MirroredSpeaker(Transform follow, Vector3 pos)
        {
            for (int k = 0; k < all.Count; k++)
            {
                Npc n = all[k];
                if (n.Char == null || n.Held || Owner(n) != Session.LocalId || !n.Char.gameObject.activeInHierarchy) continue;
                if (Session.IsHost && !n.InRange || n.Whole && !n.Engaged) continue;
                if (n.Whole || n.Ambient)
                {
                    Transform scope = n.Whole ? n.Top : n.Root;
                    if (scope == null) scope = n.Char;
                    if (follow != null ? follow.IsChildOf(scope) : (n.Char.position - pos).sqrMagnitude < 2.6f) return true;
                    continue;
                }
                foreach (Transform ph in PhonesOf(n))
                    if (ph != null && (follow != null ? follow.IsChildOf(ph) : (ph.position - pos).sqrMagnitude < 0.7f)) return true;
            }
            return false;
        }

        // Telephones du PNJ (Phone* :: Logic sous le corps), ceux que Mute coupe chez les suiveurs.
        static List<Transform> PhonesOf(Npc n)
        {
            if (n.Phones != null) return n.Phones;
            n.Phones = new List<Transform>();
            foreach (PlayMakerFSM f in n.Char.GetComponentsInChildren<PlayMakerFSM>(true))
                if (f.FsmName == "Logic" && f.gameObject.name.StartsWith("Phone")) n.Phones.Add(f.transform);
            return n.Phones;
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
            if (n == null) return "? (" + part + " non suivi)";
            var sb = new System.Text.StringBuilder(n.Path);
            sb.Append(n.Char.gameObject.activeInHierarchy ? " actif" : " inactif").Append(" en ").Append(n.Char.position.ToString("F2"));
            if (n.Moved != null) sb.Append(", ").Append(n.Moved.name).Append(" en ").Append(n.Moved.position.ToString("F2"));
            foreach (Layer L in n.Layers)
            {
                sb.Append(' ').Append(L.A != null ? L.A.name : "?").Append(':');
                int shownStates = 0;
                for (int i = 0; L.St != null && i < L.St.Length; i++)
                {
                    AnimationState st = L.St[i];
                    if (st == null || !st.enabled || st.weight <= 0.001f) continue;
                    float t = L.Loop[i] ? st.normalizedTime - Mathf.Floor(st.normalizedTime) : st.normalizedTime;
                    sb.Append(shownStates++ > 0 ? "+" : "").Append(st.name).Append('@').Append(t.ToString("F2"));
                    if (st.weight < 0.995f) sb.Append('(').Append(st.weight.ToString("F2")).Append(')');
                }
                if (shownStates == 0) sb.Append(L.Last != 0 ? "tenu " + ClipName(L, L.Last) : "-");
            }
            sb.Append(n.Held ? " (suit #" + n.Sender + ", tampon " + n.Count + ", extrapolations " + n.Extrap + ")" : " (local)");
            sb.Append(", mene par #").Append(Owner(n));
            if (n.Rules != null)
            {
                sb.Append(n.Engaged ? ", engage ici" : ", pas engage ici");
                for (int i = 0; i < n.Rules.Length; i++) if (n.RuleFsms[i] != null) sb.Append(' ').Append(n.RuleFsms[i].FsmName).Append('=').Append(n.RuleFsms[i].ActiveStateName);
            }
            if (n.Shown) sb.Append(", montre");
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
        //    avec CapturePeriode, regardent le PNJ et le capturent). [Test] PnjArrivee=s : l'invite, loin jusque-la,
        //    est teleporte pres du PNJ a s secondes (ou en PnjArriveePos=x,y,z) : il doit recevoir la pose tenue
        //    (fin du clip joue pendant son absence, comme le telephone decroche).
        //  pnjfluide : l'invite note toutes les 5 s la fluidite du PNJ suivi (vitesse moyenne, a-coups = ecart
        //    type / moyenne de la vitesse image par image, plus grand saut d'une image, tampon, extrapolations).
        //  pnj-bar : corps hors Char. Les deux cotes notent toutes les 2 s Teimo au bar ([Test] SuivrePNJ, TeimoInBar
        //    par defaut) et Teimo en luge (luge comprise). [Test] PnjLugeHeurt=1 : a 45 s l'hote fait renverser Teimo
        //    (HumanTrigger::CarHit "State 2") : l'invite doit le rejouer (ragdoll, magasin ferme).
        //  service : a [Test] ServiceT (40 s) l'invite met le PNJ [Test] ServicePNJ (Virpi) en service (automate
        //    ServiceFsm=Work, etat ServiceEtat=Move ; ServiceApproche=1 : l'invite est d'abord teleporte a 2 m de
        //    lui, a ServiceT-4 s). Attendu : invite "engage" puis "mene par #1 (moi)", hote "confie a #1" puis
        //    "suit #1" dans son etat ; a la fin du service, "rendu", l'hote reprend la main.
        //  bagarre : a 35 s l'invite allume le bagarreur [Test] BagarrePNJ (FighterPub/Fighter2/) et a 37 s met son
        //    automate Move en BagarreEtat (Chase player) : l'hote, chez qui il est absent, doit le montrer et le
        //    suivre ("montre", "suit #1").
        //  police : [Test] PoliceMaison=1 : a 40 s l'hote allume COPS et l'agent [Test] PoliceAgent (CopHome2) comme
        //    pour un joueur recherche ; les deux cotes notent l'agent toutes les 2 s (l'invite doit le voir "montre").
        //  pnj-tel : le client au telephone de la station (Teppo Sarkain, [Test] SuivrePNJ ; les deux joueurs pres de
        //    lui : TestPos=-1723.5,3.6,923.5). A 21 s chacun note une fois les parametres des actions en jeu (Anger,
        //    telephone, Speak, Move, PlayerFunctions du joueur). L'hote rend Teppo present (22 s), fait sonner son
        //    telephone (PhoneSarkain::Logic "Ringing", 24 s ; "State 4" a 34 s s'il n'est pas encore en ligne), puis a
        //    [Test] PnjInsulteT (40 s) [Test] PnjInsulteur (hote | invite) lui fait un doigt comme PlayerFunctions
        //    "Finger" : FINGER ([Test] PnjInsulteEvenement) a son automate Anger ([Test] PnjInsulteAutomate), par
        //    WorldFsms (envoye aux autres comme un vrai geste ; d'un invite, l'hote le rejoue). Les deux notent toutes
        //    les 2 s les etats des automates de Teppo et ce qui parle pres de lui (variantes MasterAudio, copies).
        static int testStep;
        static float testNext, lastShot;
        static Npc watch;
        static Vector3 fluLast;
        static bool fluHas;
        static int fluN;
        static double fluSum, fluSum2;
        static float fluMax, fluLastT;

        static string Role { get { return Session.IsHost ? "hote" : "invite"; } }

        public static void Test(string mode, float t)
        {
            if (mode == "pnj-bar") { TestBar(t); return; }
            if (mode == "service") { TestService(t); return; }
            if (mode == "bagarre") { TestFight(t); return; }
            if (mode == "police") { TestCops(t); return; }
            if (mode == "pnj-tel") { TestInsult(t); return; }
            if (mode != "pnjtel" && mode != "pnjfluide") return;
            string part = Config.Get("Test", "SuivrePNJ", "TeppoSarkain");
            float now = Time.realtimeSinceStartup;
            if (mode == "pnjtel")
            {
                if (Session.IsHost && t > 30f && testStep == 0) { testStep = 1; Log.Info("autotest : " + TestPresent(part)); }
                if (Session.IsHost && t > 40f && testStep == 1) { testStep = 2; Log.Info("autotest : " + TestRing(part)); }
                int arrive = Config.GetInt("Test", "PnjArrivee", 0);
                if (!Session.IsHost && arrive > 0 && t > arrive && testStep == 0) { testStep = 1; Log.Info("autotest : " + TestArrive(part)); }
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

        static bool Every2s(float t)
        {
            float now = Time.realtimeSinceStartup;
            if (t <= 20f || now < testNext) return false;
            testNext = now + 2f;
            return true;
        }

        static void TestBar(float t)
        {
            string part = Config.Get("Test", "SuivrePNJ", "TeimoInBar");
            if (Session.IsHost && t > 45f && testStep == 0 && Config.GetInt("Test", "PnjLugeHeurt", 0) != 0) { testStep = 1; Log.Info("autotest : pnj-bar " + TestHit()); }
            if (!Every2s(t)) return;
            int bodies = 0;
            foreach (Npc n in all) if (n.Char != null && n.Char.name != "Char") bodies++;
            Log.Info("autotest : pnj-bar (" + Role + ") " + State(part) + " | luge " + State("TeimoInSled") + " | " + HitState() + " | corps hors Char suivis " + bodies);
        }

        // Hote : Teimo en luge renverse (comme une voiture qui le percute).
        static string TestHit()
        {
            PlayMakerFSM f = HitFsm();
            if (f == null) return "pas de CarHit sous la luge";
            string before = f.ActiveStateName;
            Game.SetState(f, "State 2");
            return "luge renversee : CarHit " + before + " => " + f.ActiveStateName;
        }

        static PlayMakerFSM HitFsm()
        {
            GameObject g = Game.FindAny(HitRoots[0]);
            if (g == null) return null;
            foreach (PlayMakerFSM f in g.GetComponentsInChildren<PlayMakerFSM>(true)) if (f.FsmName == "CarHit") return f;
            return null;
        }

        static string HitState()
        {
            PlayMakerFSM f = HitFsm();
            GameObject g = Game.FindAny(HitRoots[0]);
            int rag = 0;
            if (g != null) foreach (Transform x in g.GetComponentsInChildren<Transform>(false)) if (x.name == "RagDoll") rag++;
            return "CarHit " + (f != null ? f.ActiveStateName : "?") + " (crochets " + hitHooked + ", envoyes " + hitSent + ", recus " + hitRecv + "), ragdolls actifs " + rag;
        }

        static void TestService(float t)
        {
            string part = Config.Get("Test", "ServicePNJ", "Virpi");
            int at = Config.GetInt("Test", "ServiceT", 40);
            if (!Session.IsHost && testStep == 0 && t > at - 4 && Config.GetInt("Test", "ServiceApproche", 0) != 0) { testStep = 1; Log.Info("autotest : service " + TestNear(part, 2f)); }
            if (!Session.IsHost && testStep <= 1 && t > at)
            {
                testStep = 2;
                Npc n = Find(part);
                string fsm = Config.Get("Test", "ServiceFsm", "Work"), state = Config.Get("Test", "ServiceEtat", "Move");
                PlayMakerFSM f = n != null ? FindFsm(n, fsm) : null;
                if (f == null) Log.Info("autotest : service : pas de " + fsm + " pour " + part);
                else { string before = f.ActiveStateName; Game.SetState(f, state); Log.Info("autotest : service " + Name(n) + "::" + fsm + " " + before + " => " + f.ActiveStateName); }
            }
            if (Every2s(t)) Log.Info("autotest : service (" + Role + ") " + State(part));
        }

        static void TestFight(float t)
        {
            string part = Config.Get("Test", "BagarrePNJ", "FighterPub/Fighter2/");
            Npc n = Find(part);
            if (!Session.IsHost && testStep == 0 && t > 35f)
            {
                testStep = 1;
                if (n == null) Log.Info("autotest : bagarre : " + part + " non suivi");
                else
                {
                    var on = new List<string>();
                    for (Transform x = n.Char; x != null; x = x.parent) if (!x.gameObject.activeSelf) { x.gameObject.SetActive(true); on.Add(x.name); }
                    Log.Info("autotest : bagarre : allume " + string.Join(", ", on.ToArray()));
                }
            }
            if (!Session.IsHost && testStep == 1 && t > 37f)
            {
                testStep = 2;
                PlayMakerFSM f = n != null ? FindFsm(n, "Move") : null;
                string state = Config.Get("Test", "BagarreEtat", "Chase player");
                if (f != null) { string before = f.ActiveStateName; Game.SetState(f, state); Log.Info("autotest : bagarre Move " + before + " => " + f.ActiveStateName); }
            }
            if (Every2s(t)) Log.Info("autotest : bagarre (" + Role + ") " + State(part));
        }

        static void TestCops(float t)
        {
            string part = Config.Get("Test", "PoliceAgent", "CopHome2");
            if (Session.IsHost && testStep == 0 && t > 40f && Config.GetInt("Test", "PoliceMaison", 0) != 0)
            {
                testStep = 1;
                Npc n = Find(part);
                if (n == null) Log.Info("autotest : police : agent " + part + " non suivi");
                else
                {
                    var on = new List<string>();
                    for (Transform x = n.Char; x != null; x = x.parent) if (!x.gameObject.activeSelf) { x.gameObject.SetActive(true); on.Add(x.name); }
                    Log.Info("autotest : police : agents a domicile allumes (" + string.Join(", ", on.ToArray()) + ")");
                }
            }
            if (Every2s(t)) Log.Info("autotest : police agent (" + Role + ") " + State(part));
        }

        static readonly string[] InCall = { "Get clip", "State 2", "State 3", "State 8", "In phone" };

        static void TestInsult(float t)
        {
            string part = Config.Get("Test", "SuivrePNJ", "TeppoSarkain");
            Npc n = Find(part);
            if (testStep == 0 && t > 21f) { testStep = 1; DumpSpeech(n); }
            if (Session.IsHost && testStep == 1 && t > 22f) { testStep = 2; Log.Info("autotest : pnj-tel (hote) " + TestPresent(part)); }
            if (Session.IsHost && testStep == 2 && t > 24f) { testStep = 3; Log.Info("autotest : pnj-tel (hote) sonnerie : " + Phone(n, "Ringing")); }
            if (Session.IsHost && testStep == 3 && t > 34f)
            {
                testStep = 4;
                PlayMakerFSM f = n != null ? PhoneFsm(n) : null;
                if (f != null && System.Array.IndexOf(InCall, f.ActiveStateName) < 0) Log.Info("autotest : pnj-tel (hote) pas encore en ligne, decroche : " + Phone(n, "State 4"));
            }
            bool insulter = (Config.Get("Test", "PnjInsulteur", "hote") == "invite") != Session.IsHost;
            if (insulter && testStep < 9 && t > Config.GetInt("Test", "PnjInsulteT", 40)) { testStep = 9; Log.Info("autotest : pnj-tel (" + Role + ") doigt : " + Insult(n, part)); }
            if (Every2s(t)) Log.Info("autotest : pnj-tel (" + Role + ") " + Speech(n));
        }

        static PlayMakerFSM PhoneFsm(Npc n)
        {
            foreach (Transform ph in PhonesOf(n)) if (ph != null) { PlayMakerFSM f = Game.FsmOn(ph.gameObject, "Logic"); if (f != null) return f; }
            return null;
        }

        static string Phone(Npc n, string state)
        {
            PlayMakerFSM f = n != null ? PhoneFsm(n) : null;
            if (f == null) return "pas de telephone (Phone* :: Logic)";
            string before = f.ActiveStateName;
            Game.SetState(f, state);
            return f.gameObject.name + "::Logic " + before + " => " + f.ActiveStateName;
        }

        // Comme le doigt du joueur (PlayerFunctions "Finger") : l'evenement a l'automate de colere du PNJ, par WorldFsms
        // (qui l'envoie aux autres comme un vrai geste) ; s'il ne le suit pas, directement.
        static string Insult(Npc n, string part)
        {
            string fsm = Config.Get("Test", "PnjInsulteAutomate", "Anger"), ev = Config.Get("Test", "PnjInsulteEvenement", "FINGER");
            PlayMakerFSM f = n != null ? FindFsm(n, fsm) : null;
            if (f == null) return "pas d'automate " + fsm + " pour " + part;
            string r = WorldFsms.TestEvent(f.gameObject.name + "::" + fsm, ev);
            if (r.StartsWith("rien pour"))
            {
                string before = f.ActiveStateName;
                f.SendEvent(ev);
                r = "direct (automate pas suivi par le monde) " + before + " -" + ev + "-> " + f.ActiveStateName;
            }
            // En jeu, Move "Look at plaer" met Angry au telephone (il raccroche en colere) ; Move n'y va que si Teppo
            // est a l'arret pres du joueur, ce qu'un essai ne garantit pas : [Test] PnjInsulteForcer=1 le fait ici.
            PlayMakerFSM ph = PhoneFsm(n);
            if (ph != null && Config.GetInt("Test", "PnjInsulteForcer", 1) == 1)
            {
                FsmBool angry = ph.FsmVariables.GetFsmBool("Angry");
                if (angry != null) { angry.Value = true; r += " ; Angry force au telephone"; }
            }
            return r + " ; telephone " + StateOf(ph);
        }

        static string StateOf(PlayMakerFSM f)
        {
            if (f == null) return "-";
            string s = f.ActiveStateName;
            return (string.IsNullOrEmpty(s) ? "?" : s) + (f.enabled ? "" : "(coupe)");
        }

        static string Speech(Npc n)
        {
            if (n == null || n.Char == null) return "aucun PNJ suivi";
            var sb = new System.Text.StringBuilder(Name(n));
            sb.Append(n.Char.gameObject.activeInHierarchy ? " present" : " absent");
            sb.Append(" tel=").Append(StateOf(PhoneFsm(n))).Append(" colere=").Append(StateOf(FindFsm(n, "Anger")))
              .Append(" marche=").Append(StateOf(FindFsm(n, "Move"))).Append(" parole=").Append(StateOf(FindFsm(n, "Speak")));
            foreach (GameObject p in n.Props) if (p != null && p.name == "PhoneMeshHand") sb.Append(p.activeSelf ? ", telephone en main" : ", telephone range");
            sb.Append(" | voix ").Append(Voices.AudioNear(n.Char.position, 4f));
            sb.Append(" | ").Append(n.Held ? "suit #" + n.Sender + " (" + (n.Muted != null ? n.Muted.Count : 0) + " automates coupes)" : "local");
            return sb.ToString();
        }

        // Parametres des actions des automates en jeu (non visibles dans le vidage) : cible du FINGER du joueur, de
        // l'evenement qu'Anger renvoie, groupes MasterAudio et objet suivi des repliques.
        static void DumpSpeech(Npc n)
        {
            var fsms = new List<PlayMakerFSM>();
            if (n != null) { fsms.Add(FindFsm(n, "Anger")); fsms.Add(PhoneFsm(n)); fsms.Add(FindFsm(n, "Speak")); fsms.Add(FindFsm(n, "Move")); }
            GameObject pl = GameObject.Find("PLAYER");
            if (pl != null) foreach (PlayMakerFSM f in pl.GetComponentsInChildren<PlayMakerFSM>(true)) if (f.FsmName == "PlayerFunctions") { fsms.Add(f); break; }
            foreach (PlayMakerFSM f in fsms)
            {
                if (f == null) continue;
                try { f.Fsm.InitData(); } catch { }
                foreach (FsmState st in f.Fsm.States)
                {
                    if (f.FsmName == "PlayerFunctions" && st.Name != "Finger" && st.Name != "Fuck singer" && st.Name != "State 2") continue;
                    if (f.FsmName == "Move" && st.Name != "Look at plaer") continue;
                    var sb = new System.Text.StringBuilder();
                    try { foreach (FsmStateAction a in st.Actions) if (a != null && !(a is ModHook)) sb.Append(Params(a)).Append("; "); }
                    catch (System.Exception e) { sb.Append("? ").Append(e.Message); }
                    string s = sb.ToString();
                    if (s.Length > 1500) s = s.Substring(0, 1500) + "...";
                    Log.Info("autotest : pnj-tel parametres " + f.gameObject.name + "::" + f.FsmName + " \"" + st.Name + "\" : " + s);
                }
            }
        }

        static string Params(FsmStateAction a)
        {
            var sb = new System.Text.StringBuilder(a.GetType().Name).Append('(');
            bool first = true;
            foreach (System.Reflection.FieldInfo fi in a.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                string v = Param(fi.GetValue(a), 0);
                if (v == null) continue;
                if (!first) sb.Append(", ");
                first = false;
                sb.Append(fi.Name).Append('=').Append(v);
            }
            return sb.Append(')').ToString();
        }

        static string Param(object v, int depth)
        {
            if (v == null) return null;
            var nv = v as NamedVariable;
            if (nv != null && nv.UseVariable) return "{" + nv.Name + "}";
            if (v is FsmString) return "\"" + ((FsmString)v).Value + "\"";
            if (v is FsmFloat) return ((FsmFloat)v).Value.ToString("0.##");
            if (v is FsmInt) return ((FsmInt)v).Value.ToString();
            if (v is FsmBool) return ((FsmBool)v).Value ? "oui" : "non";
            if (v is FsmGameObject) { GameObject g = ((FsmGameObject)v).Value; return g != null ? Recon.Path(g.transform) : "null"; }
            if (v is FsmEvent) return "ev:" + ((FsmEvent)v).Name;
            if (v is FsmOwnerDefault)
            {
                var od = (FsmOwnerDefault)v;
                return od.OwnerOption == OwnerDefaultOption.UseOwner ? "soi" : Param(od.GameObject, depth);
            }
            if (v is string || v is bool || v is float || v is int || v is System.Enum) return v.ToString();
            if (v is Object) { var o = (Object)v; return o != null ? o.name : "null"; }
            if (depth > 0) return null;
            var arr = v as System.Array;
            if (arr != null)
            {
                var parts = new List<string>();
                foreach (object e in arr) { if (parts.Count >= 8) break; string s = Param(e, depth + 1); if (s != null) parts.Add(s); }
                return "[" + string.Join(" ", parts.ToArray()) + "]";
            }
            string ns = v.GetType().Namespace;
            if (ns == null || !ns.StartsWith("HutongGames")) return null;
            var sb = new System.Text.StringBuilder("{");
            foreach (System.Reflection.FieldInfo fi in v.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                string s = Param(fi.GetValue(v), depth + 1);
                if (s != null) sb.Append(fi.Name).Append('=').Append(s).Append(' ');
            }
            return sb.ToString().TrimEnd() + "}";
        }

        // Invite : teleporte a 'dist' m devant le PNJ.
        static string TestNear(string part, float dist)
        {
            Npc n = Find(part);
            GameObject pl = GameObject.Find("PLAYER");
            if (n == null || pl == null) return "approche impossible (" + (n == null ? "aucun PNJ " + part : "pas de joueur") + ")";
            Vector3 to = n.Char.position + n.Char.forward * dist + Vector3.up * 0.5f;
            var cc = pl.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            pl.transform.position = to;
            if (cc != null) cc.enabled = true;
            return "approche de " + n.Path + " en " + to.ToString("F1");
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

        // Invite : teleporte derriere le PNJ (2 m, ou en [Test] PnjArriveePos), comme le fait TestPos.
        static string TestArrive(string part)
        {
            Npc n = Find(part);
            GameObject pl = GameObject.Find("PLAYER");
            if (n == null || pl == null) return "pnjtel : arrivee impossible (" + (n == null ? "aucun PNJ " + part : "pas de joueur") + ")";
            Vector3 to = n.Char.position - n.Char.forward * 2f + Vector3.up * 0.5f;
            string[] c = Config.Get("Test", "PnjArriveePos", "").Split(',');
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            if (c.Length == 3) to = new Vector3(float.Parse(c[0], ci), float.Parse(c[1], ci), float.Parse(c[2], ci));
            var cc = pl.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            pl.transform.position = to;
            if (cc != null) cc.enabled = true;
            return "pnjtel : arrivee de l'invite en " + to.ToString("F1") + " pres de " + n.Path;
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

        // Suiveur, apres la pose : deplacement du corps suivi d'une image a l'autre.
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
            return watch.Path + (watch.Held ? " (suit #" + watch.Sender + ")" : " (local)") + " : vitesse moyenne " + mean.ToString("F2") + " m/s, a-coups "
                   + (mean > 0.05 ? (sd / mean).ToString("F2") : "-") + ", plus grand saut " + fluMax.ToString("F3") + " m sur " + fluN
                   + " images, tampon " + watch.Count + ", extrapolations " + watch.Extrap + ", decalage " + ClockOf(watch.Sender).Off.ToString("F3") + " s";
        }
    }
}
