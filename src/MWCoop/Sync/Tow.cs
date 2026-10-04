using System.Collections.Generic;
using System.Text;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Corde de remorquage entre deux vehicules.
    // Dans le jeu : chaque crochet (<voiture>/HookFront, HookRear, KEKMET/.../RopePoint/HookFront...) a un automate
    // 'Logic'. Un clic ('State 3') sort la corde du joueur LOCAL (PLAYER/.../FPSCamera/TowingRope/Rope, automate
    // 'Status') et y accroche un bout : 1er clic 'Activate cable' (RopeFirst sur ce crochet, Attached, Hook1),
    // 2e clic sur un autre crochet 'Activate cable 2' (RopeSecond, Hook2). 'Status' ajoute alors un SpringJoint
    // au porteur du 1er crochet (Car1 = son parent), relie au corps du 2e (Car2Rigidbody) : ancres = positions
    // locales des crochets, longueur = celle de la corde tendue, casse a BreakingForce (55000). 'Wait detach' le
    // detruit quand Attached repasse a faux (clic "REMOVE TOWING ROPE" : 'Remove rope') ou quand il a casse,
    // puis 'Snap off' range la corde. Une seule corde par joueur, et elle n'existe que chez lui : sans ce module
    // la voiture remorquee n'est tiree que chez celui qui a accroche, et le recalage de l'hote la ramene ailleurs.
    //  - Le proprietaire (celui qui a accroche) annonce les deux crochets (chemins) et les reglages du joint
    //    (ancres, ressort, amortisseur, longueur, casse), puis toutes les 5 s (les arrivants l'ont ainsi) ; au
    //    decrochage ou a la casse chez lui, il annonce la fin. Une copie sans annonce depuis 15 s est retiree.
    //  - Ailleurs : copie du joint sur les memes corps, en ConfigurableJoint (limite lineaire spherique a
    //    ressort : meme effet que le SpringJoint). Jamais un SpringJoint : 'Status' de la corde LOCALE, s'il
    //    s'accroche au meme corps, detruirait la copie a la place du sien (DestroyComponent et HasComponent
    //    prennent le premier SpringJoint de l'objet). Plus une corde visible (clone sans automate de la corde
    //    locale, bouts poses sur les crochets). Le joint n'est fait que crochets a portee (une voiture pas
    //    encore recalee serait tiree a travers la carte) ; hors de portee de plus de 3 m sans autorite (voiture
    //    recalee d'un coup), il est retire puis refait.
    //  - Qui tire : celui qui conduit un bout, quand l'autre n'est conduit par personne, fait avancer la voiture
    //    remorquee chez tous : il envoie sa pose 10 fois/s en etat 2 de VehicleSync (comme un moteur laisse
    //    tournant) et ailleurs elle devient une copie cinematique qui le suit ; a la fin une derniere pose
    //    (etat 0) la rend a l'hote. Deux conducteurs (l'un tire, l'autre braque la voiture tiree) : chez celui
    //    de tete la corde est detendue (sa voiture tirerait sinon sur une copie cinematique en retard, de masse
    //    infinie, et la corde casserait) ; chez celui de derriere elle tire sa voiture vers la copie de tete.
    //  - Retirer la corde d'un autre : un clic sur un crochet de sa corde (le jeu y accroche alors le 1er bout de
    //    la corde locale) la fait retirer par son proprietaire ; la corde locale est rangee aussitot. Une casse
    //    chez celui qui fait autorite sur un bout (conducteur, tireur, hote si garee) la fait retirer aussi ;
    //    ailleurs le joint casse est simplement refait.
    //  Les automates 'Logic' des crochets et 'Status' de la corde sont reserves a ce module : le monde rejouait
    //  sinon les clics sur les crochets du taxi (sous JOBS), et la corde de l'autre s'y accrochait.
    public static class Tow
    {
        const string Module = "remorquage";
        const int DETACH = 0, ATTACH = 1, REMOVE = 2, BROKEN = 3;   // Msg.Tow

        class Rope
        {
            public int Owner;                          // joueur dont c'est la corde
            public bool Mirror;                        // copie de la corde d'un autre joueur
            public string Path1, Path2;
            public Transform Hook1, Hook2;
            public Rigidbody Body1, Body2;             // porteur de chaque crochet (Body2 null : relie au monde)
            public int Car1 = -1, Car2 = -1;           // voiture (VehicleSync) de chaque bout, -1 : aucune
            public Joint Joint;                        // SpringJoint du jeu (notre corde) ou ConfigurableJoint (copie)
            public Vector3 Anchor, ConnAnchor;
            public float Spring, Damper, MaxDist, BreakForce, BreakTorque;
            public bool Collide;
            public float Seen, BrokeAt = -100f, NextWait;
            public bool Gone;                          // copie : joint retire expres (detendu, hors de portee, casse signalee)
            public GameObject Visual, End1, End2;      // corde visible de la copie
            public int Carry = -1;                     // voiture qu'on fait avancer chez tous (etat 2)
            public bool Soft;                          // detendue ici (tete d'un remorquage a deux conducteurs)
        }

        static Rope local;                                                 // notre corde, accrochee
        static readonly Dictionary<int, Rope> mirrors = new Dictionary<int, Rope>();
        static readonly List<Rope> all = new List<Rope>();                 // la notre et les copies
        static readonly Dictionary<string, Transform> hooks = new Dictionary<string, Transform>();   // crochets par chemin
        static GameObject ropeGo, template, prevHook1;
        static PlayMakerFSM ropeFsm;
        static FsmBool rAttached;
        static FsmGameObject rHook1, rHook2;
        static FsmObject rSpring;
        static FsmFloat rBreak;
        static float scanAt = -1, nextPoll, nextKeep, nextTemplate, nextWarn;
        static bool softWarned;

        static Tow()
        {
            Session.PlayerLeft += OnPlayerLeft;
        }

        public static void OnLevelLoaded()
        {
            local = null; mirrors.Clear(); all.Clear(); hooks.Clear();
            ropeGo = template = prevHook1 = null; ropeFsm = null;
            rAttached = null; rHook1 = rHook2 = null; rSpring = null; rBreak = null;
            testStep = 0; testLog = 0; testH1 = testH2 = null;
            // Avant Jobs (10 s) et le monde (16 s) : les automates des crochets sont reserves ici d'abord.
            scanAt = PlayerSync.InGame ? Time.realtimeSinceStartup + 9f : -1;
        }

        public static void Update()
        {
            if (!Session.Active || scanAt < 0) return;
            float now = Time.realtimeSinceStartup;
            if (now >= scanAt) { Scan(); scanAt = ropeFsm == null ? now + 20f : float.MaxValue; }   // corde introuvable : on reessaie
            if (now < nextPoll) return;
            nextPoll = now + 0.1f;
            if (template == null && ropeGo != null && now >= nextTemplate) { nextTemplate = now + 5f; MakeTemplate(); }
            if (ropeFsm != null) PollLocal(now);
            for (int i = all.Count - 1; i >= 0; i--) if (all[i].Mirror) Keep(all[i], now);
            for (int i = 0; i < all.Count; i++) Lead(all[i]);
            for (int i = 0; i < all.Count; i++)
            {
                int c = all[i].Carry;
                if (c < 0) continue;
                bool dup = false;
                for (int k = 0; k < i; k++) if (all[k].Carry == c) dup = true;
                if (!dup) SendCar(c, 2, false);
            }
        }

        // Voiture (index VehicleSync) que nous faisons avancer chez tous au bout d'une corde. VehicleSync doit
        // l'exclure de son recalage de l'hote toutes les 2 s (voir le rapport du lot 2).
        public static bool Carries(int car)
        {
            if (car < 0) return false;
            for (int i = 0; i < all.Count; i++) if (all[i].Carry == car) return true;
            return false;
        }

        // ---------------------------------------------------------------- releve
        static void Scan()
        {
            int claimed = 0;
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var f = (PlayMakerFSM)o;
                if (f.FsmName != "Logic" || f.hideFlags != HideFlags.None || !f.transform.root.gameObject.activeInHierarchy) continue;
                FsmGameObject rope = f.FsmVariables.FindFsmGameObject("Rope");
                if (rope == null || f.FsmVariables.FindFsmGameObject("ThisCar") == null) continue;
                if (Replay.Claim(f, Module)) claimed++;
                hooks[Recon.Path(f.transform)] = f.transform;
                if (ropeGo == null && rope.Value != null) ropeGo = rope.Value;
            }
            if (ropeGo != null && ropeFsm == null)
            {
                PlayMakerFSM f = Game.FsmOn(ropeGo, "Status");
                if (f != null)
                {
                    FsmVariables v = f.FsmVariables;
                    rAttached = v.FindFsmBool("Attached"); rHook1 = v.FindFsmGameObject("Hook1"); rHook2 = v.FindFsmGameObject("Hook2");
                    rSpring = v.FindFsmObject("Spring"); rBreak = v.FindFsmFloat("BreakingForce");
                    if (rAttached != null && rHook1 != null && rHook2 != null && rSpring != null) { ropeFsm = f; Replay.Claim(f, Module); }
                }
            }
            MakeTemplate();
            Log.Info("remorquage : " + claimed + " crochets, corde " + (ropeFsm != null ? Recon.Path(ropeGo.transform) : "introuvable")
                     + (template != null ? ", modele de corde visible pret" : ""));
        }

        // Modele de la corde visible des copies : clone de la corde locale rangee (ses deux bouts dessous), sans automate.
        static void MakeTemplate()
        {
            if (template != null || ropeGo == null || ropeGo.activeSelf) return;
            if (ropeGo.transform.Find("RopeFirst") == null || ropeGo.transform.Find("RopeSecond") == null) return;   // bouts accroches
            var g = (GameObject)Object.Instantiate(ropeGo);   // inactive comme l'original : rien n'y demarre
            g.name = "MWCoop-CordeModele";
            foreach (PlayMakerFSM f in g.GetComponentsInChildren<PlayMakerFSM>(true)) Object.DestroyImmediate(f);
            template = g;
        }

        // ---------------------------------------------------------------- notre corde
        static void PollLocal(float now)
        {
            SpringJoint j = rSpring.Value as SpringJoint;
            GameObject h1 = rHook1.Value, h2 = rHook2.Value;
            bool on = rAttached.Value && j != null && h1 != null && h2 != null;
            if (on && (local == null || local.Joint != j || local.Hook1 != h1.transform || local.Hook2 != h2.transform))
            {
                if (local != null) Drop(local);
                local = FromGame(j, h1.transform, h2.transform);
                all.Add(local);
                SendAttach(local);
                nextKeep = now + 5f;
                Log.Info("remorquage : corde accrochee " + local.Path1 + " <-> " + local.Path2 + " (" + Settings(local) + ")");
            }
            else if (!on && local != null)
            {
                Log.Info("remorquage : corde decrochee (" + local.Path1 + " <-> " + local.Path2 + ")");
                Session.SendAll(new NetWriter(Msg.Tow).U8(DETACH).U8(Session.LocalId), true);
                Drop(local);
                local = null;
            }
            else if (on && now >= nextKeep) { nextKeep = now + 5f; SendAttach(local); }

            // Clic sur un crochet de la corde d'un autre : le jeu vient d'y accrocher le 1er bout de NOTRE corde.
            // C'est une demande de retrait : son proprietaire la retire, la notre est rangee.
            if (rAttached.Value && h2 == null && h1 != null && h1 != prevHook1)
            {
                Rope m = MirrorAt(h1.transform);
                if (m != null)
                {
                    Session.SendAll(new NetWriter(Msg.Tow).U8(REMOVE).U8(m.Owner).U8(Session.LocalId), true);
                    rAttached.Value = false;   // 'Check distance' -> 'Snap off' ; le crochet -> 'Remove rope'
                    if (ropeFsm.ActiveStateName == "Wait") Game.SetState(ropeFsm, "Snap off");
                    Hud.Toast("Corde de " + Name(m.Owner) + " retiree");
                    Log.Info("remorquage : clic sur " + m.Path1 + " : retrait de la corde de " + Name(m.Owner) + " demande");
                }
            }
            prevHook1 = rAttached.Value ? h1 : null;
        }

        static Rope FromGame(SpringJoint j, Transform h1, Transform h2)
        {
            var r = new Rope { Owner = Session.LocalId, Hook1 = h1, Hook2 = h2, Path1 = Recon.Path(h1), Path2 = Recon.Path(h2), Joint = j,
                               Body1 = j.GetComponent<Rigidbody>(), Body2 = j.connectedBody,
                               Anchor = j.anchor, ConnAnchor = j.connectedAnchor, Spring = j.spring, Damper = j.damper, MaxDist = j.maxDistance,
                               Collide = j.enableCollision };
            // La casse de l'automate (le joint d'une copie est rendu incassable par VehicleSync).
            r.BreakForce = rBreak != null ? rBreak.Value : j.breakForce;
            r.BreakTorque = rBreak != null ? rBreak.Value : j.breakTorque;
            r.Car1 = CarOf(r.Body1);
            r.Car2 = CarOf(r.Body2);
            return r;
        }

        static void SendAttach(Rope r)
        {
            var w = new NetWriter(Msg.Tow).U8(ATTACH).U8(Session.LocalId).Str(r.Path1).Str(r.Path2).Vec(r.Anchor).Vec(r.ConnAnchor)
                .F32(r.Spring).F32(r.Damper).F32(r.MaxDist).F32(r.BreakForce).F32(r.BreakTorque).U8(r.Collide ? 1 : 0);
            Session.SendAll(w, true);
        }

        // Proprietaire : un autre demande le retrait, ou la corde a casse chez lui.
        static void RemoveLocal(int by, int kind)
        {
            if (local == null || rAttached == null || !rAttached.Value) return;
            Log.Info("remorquage : corde " + (kind == BROKEN ? "cassee chez " : "retiree par ") + Name(by));
            Hud.Toast(kind == BROKEN ? "La corde a casse" : Name(by) + " a retire la corde");
            rAttached.Value = false;   // comme 'Remove rope' : 'Wait detach' detruit le joint, 'Snap off' range la corde
        }

        // ---------------------------------------------------------------- copies
        public static void OnMessage(Peer from, NetReader r)
        {
            int kind = r.U8();
            int owner = r.U8();
            if (kind == REMOVE || kind == BROKEN)
            {
                int by = r.U8();
                if (Session.IsHost)
                {
                    by = from.Id;
                    if (owner != Session.LocalId) Session.Broadcast(new NetWriter(Msg.Tow).U8(kind).U8(owner).U8(by), true, from.Id);
                }
                if (owner == Session.LocalId) RemoveLocal(by, kind);
                return;
            }
            if (Session.IsHost)
            {
                owner = from.Id;   // un invite ne parle que de sa corde
                Session.Broadcast(new NetWriter(Msg.Tow).U8(kind).U8(owner).Raw(r.Rest()), true, from.Id);
            }
            if (owner == Session.LocalId) return;
            Rope m;
            mirrors.TryGetValue(owner, out m);
            if (kind == DETACH)
            {
                if (m != null) { Log.Info("remorquage : " + Name(owner) + " a decroche sa corde"); Drop(m); }
                return;
            }
            if (kind != ATTACH) return;
            string p1 = r.Str(), p2 = r.Str();
            Vector3 a = r.Vec(), ca = r.Vec();
            float spring = r.F32(), damper = r.F32(), max = r.F32(), bf = r.F32(), bt = r.F32();
            bool collide = r.U8() != 0;
            float now = Time.realtimeSinceStartup;
            if (m != null && m.Path1 == p1 && m.Path2 == p2) { m.Seen = now; return; }   // annonce periodique
            if (scanAt < 0) return;                                                     // pas en jeu ici
            if (m != null) Drop(m);
            m = new Rope { Owner = owner, Mirror = true, Path1 = p1, Path2 = p2, Anchor = a, ConnAnchor = ca, Spring = spring, Damper = damper,
                           MaxDist = max, BreakForce = bf, BreakTorque = bt, Collide = collide, Seen = now, Gone = true };
            m.Hook1 = HookAt(p1);
            m.Hook2 = HookAt(p2);
            if (m.Hook1 != null && m.Hook1.parent != null) m.Body1 = m.Hook1.parent.GetComponent<Rigidbody>();
            if (m.Hook2 != null && m.Hook2.parent != null) m.Body2 = m.Hook2.parent.GetComponent<Rigidbody>();
            if (m.Hook1 == null || m.Hook2 == null || m.Body1 == null)
            {
                // Pas encore charge ici (arrivant) : a la prochaine annonce.
                if (now >= nextWarn) { nextWarn = now + 10f; Log.Warn("remorquage : corde de " + Name(owner) + " : crochets " + p1 + " / " + p2 + " introuvables ici"); }
                return;
            }
            m.Car1 = CarOf(m.Body1);
            m.Car2 = CarOf(m.Body2);
            mirrors[owner] = m;
            all.Add(m);
            Log.Info("remorquage : corde de " + Name(owner) + " : " + p1 + " <-> " + p2 + " (" + Settings(m) + ")");
            Keep(m, now);
        }

        // Copie, 10 fois/s : annonce trop vieille, objets detruits, casse, portee, joint et corde visible a faire.
        static void Keep(Rope m, float now)
        {
            if (now - m.Seen > 15f) { Log.Info("remorquage : corde de " + Name(m.Owner) + " plus annoncee, retiree"); Drop(m); return; }
            if (m.Hook1 == null || m.Hook2 == null || m.Body1 == null) { Log.Info("remorquage : corde de " + Name(m.Owner) + " : crochet detruit, retiree"); Drop(m); return; }
            float d = Vector3.Distance(m.Hook1.position, m.Hook2.position);
            bool auth = Authoritative(m);
            if (!m.Gone && m.Joint == null)
            {
                // Casse ici (Unity detruit le joint casse).
                m.Gone = true;
                StopCarry(m);   // la voiture tiree est libre : derniere pose
                if (auth)
                {
                    m.BrokeAt = now;
                    Session.SendAll(new NetWriter(Msg.Tow).U8(BROKEN).U8(m.Owner).U8(Session.LocalId), true);
                    Log.Info("remorquage : corde de " + Name(m.Owner) + " cassee ici (" + d.ToString("F1") + " m) : retrait demande");
                }
                else Log.Info("remorquage : corde de " + Name(m.Owner) + " cassee ici sans autorite (" + d.ToString("F1") + " m) : refaite");
            }
            if (m.Joint != null && !auth && d > m.MaxDist + 3f)
            {
                Object.Destroy(m.Joint);
                m.Joint = null; m.Gone = true;
                Log.Info("remorquage : corde de " + Name(m.Owner) + " hors de portee (" + d.ToString("F1") + " m), joint retire en attendant");
            }
            if (m.Joint == null && !m.Soft && now - m.BrokeAt > 10f)
            {
                if (d <= m.MaxDist + 0.5f) MakeJoint(m);
                else if (now >= m.NextWait) { m.NextWait = now + 10f; Log.Info("remorquage : corde de " + Name(m.Owner) + " en attente, crochets a " + d.ToString("F1") + " m"); }
            }
            if (m.Visual == null && d <= m.MaxDist + 3f) MakeVisual(m);
        }

        static void MakeJoint(Rope m)
        {
            var j = m.Body1.gameObject.AddComponent<ConfigurableJoint>();
            j.autoConfigureConnectedAnchor = false;
            j.connectedBody = m.Body2;
            j.anchor = m.Anchor;
            j.connectedAnchor = m.ConnAnchor;
            j.xMotion = ConfigurableJointMotion.Limited;
            j.yMotion = ConfigurableJointMotion.Limited;
            j.zMotion = ConfigurableJointMotion.Limited;
            SoftJointLimit lim = j.linearLimit;
            lim.limit = m.MaxDist;
            j.linearLimit = lim;
            SoftJointLimitSpring spr = j.linearLimitSpring;
            spr.spring = m.Spring;
            spr.damper = m.Damper;
            j.linearLimitSpring = spr;
            j.enableCollision = m.Collide;
            j.breakForce = m.BreakForce;
            j.breakTorque = m.BreakTorque;
            m.Joint = j;
            m.Gone = false;
        }

        static void MakeVisual(Rope m)
        {
            if (template == null) return;
            var g = (GameObject)Object.Instantiate(template);
            g.name = "MWCoop-Corde-" + m.Owner;
            Transform a = g.transform.Find("RopeFirst"), b = g.transform.Find("RopeSecond");
            if (a == null || b == null)
            {
                // Modele abime : refait par MakeTemplate (5 s), pas un clone par passage.
                Object.Destroy(g); Object.Destroy(template); template = null;
                Log.Warn("remorquage : modele de corde sans ses bouts, refait");
                return;
            }
            Pin(a, m.Hook1); Pin(b, m.Hook2);
            a.name = "MWCoop-CordeBout1"; b.name = "MWCoop-CordeBout2";
            foreach (SkinnedMeshRenderer s in a.GetComponentsInChildren<SkinnedMeshRenderer>(true)) s.updateWhenOffscreen = true;   // tendue loin de son os racine
            m.Visual = g; m.End1 = a.gameObject; m.End2 = b.gameObject;
        }

        static void Pin(Transform t, Transform hook)
        {
            t.parent = hook;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
        }

        static void Drop(Rope r)
        {
            StopCarry(r);
            if (r.Soft && !r.Mirror) SetSoft(r, false);   // notre corde : reglages du jeu rendus
            if (r.Mirror && r.Joint != null) Object.Destroy(r.Joint);
            if (r.End1 != null) Object.Destroy(r.End1);
            if (r.End2 != null) Object.Destroy(r.End2);
            if (r.Visual != null) Object.Destroy(r.Visual);
            r.Joint = null;
            all.Remove(r);
            Rope cur;
            if (r.Mirror && mirrors.TryGetValue(r.Owner, out cur) && cur == r) mirrors.Remove(r.Owner);
        }

        static void OnPlayerLeft(PlayerInfo pi)
        {
            Rope m;
            if (!mirrors.TryGetValue(pi.Id, out m)) return;
            Log.Info("remorquage : " + pi.Name + " parti, sa corde est retiree");
            Drop(m);
        }

        // Crochet par son chemin : releve du chargement, sinon recherche dans la scene (crochet cree depuis).
        static Transform HookAt(string path)
        {
            Transform t;
            if (hooks.TryGetValue(path, out t) && t != null) return t;
            GameObject g = Game.FindAny(path);
            if (g == null) return null;
            hooks[path] = g.transform;
            return g.transform;
        }

        static Rope MirrorAt(Transform hook)
        {
            foreach (Rope m in mirrors.Values) if (m.Hook1 == hook || m.Hook2 == hook) return m;
            return null;
        }

        // Un bout est a nous : conduit ou tire ici, ou gare (hote).
        static bool Authoritative(Rope r)
        {
            return r.Carry >= 0 || VehicleSync.Authority(r.Car1) == Session.LocalId || VehicleSync.Authority(r.Car2) == Session.LocalId;
        }

        // ---------------------------------------------------------------- qui tire
        static void Lead(Rope r)
        {
            bool h1 = Driven(r.Car1), h2 = Driven(r.Car2);
            int want = -1;
            if (r.Joint != null)
            {
                if (h1 && !h2 && r.Car2 >= 0 && r.Car2 != r.Car1 && !VehicleSync.IsCopy(r.Car2)) want = r.Car2;
                else if (h2 && !h1 && r.Car1 >= 0 && r.Car1 != r.Car2 && !VehicleSync.IsCopy(r.Car1)) want = r.Car1;
            }
            if (want != r.Carry)
            {
                StopCarry(r);
                r.Carry = want;
                if (want >= 0) Log.Info("remorquage : on tire " + CarName(want) + " (sa pose part d'ici)");
            }
            // Deux conducteurs : on est en tete si notre voiture roule et s'eloigne de l'autre bout.
            Rigidbody mine = null, other = null;
            if (h1 && !h2 && VehicleSync.IsCopy(r.Car2)) { mine = VehicleSync.CarBody(r.Car1); other = VehicleSync.CarBody(r.Car2); }
            else if (h2 && !h1 && VehicleSync.IsCopy(r.Car1)) { mine = VehicleSync.CarBody(r.Car2); other = VehicleSync.CarBody(r.Car1); }
            bool soft = false;
            if (mine != null && other != null)
            {
                Vector3 v = mine.velocity;
                float sp = v.magnitude, ahead = Vector3.Dot(v, mine.position - other.position);
                soft = ahead > 0f && sp > (r.Soft ? 1f : 2f);
            }
            if (soft != r.Soft) SetSoft(r, soft);
        }

        // Conduite ici (ou quittee moteur tournant), pas seulement tiree par nous (si VehicleSync.DrivenHere compte un jour
        // les voitures tirees, la voiture tiree ne doit pas passer pour la tete).
        static bool Driven(int car) { return VehicleSync.DrivenHere(car) && !Carries(car); }

        static void StopCarry(Rope r)
        {
            int c = r.Carry;
            r.Carry = -1;
            if (c < 0) return;
            // Rendue : derniere pose sans conducteur (sauf si un autre la fait deja avancer, ou si on la conduit).
            if (!VehicleSync.IsCopy(c) && !VehicleSync.DrivenHere(c) && !Carries(c)) SendCar(c, 0, true);
            Log.Info("remorquage : on ne tire plus " + CarName(c));
        }

        static void SetSoft(Rope r, bool on)
        {
            if (r.Mirror)
            {
                r.Soft = on;
                if (on && r.Joint != null) { Object.Destroy(r.Joint); r.Joint = null; r.Gone = true; }   // refait par Keep
            }
            else
            {
                var sj = r.Joint as SpringJoint;
                if (sj == null) { r.Soft = false; return; }
                // Ancre reliee recalculee par Unity a chaque changement : on ne touche pas a ce joint-la.
                if (sj.autoConfigureConnectedAnchor)
                {
                    if (!softWarned) { softWarned = true; Log.Warn("remorquage : joint du jeu a ancre automatique, pas detendu"); }
                    r.Soft = false;
                    return;
                }
                r.Soft = on;
                sj.spring = on ? 0f : r.Spring;
                sj.maxDistance = on ? 1000f : r.MaxDist;
            }
            Log.Info("remorquage : corde " + (on ? "detendue ici (on tire en tete, l'autre conducteur la sent chez lui)" : "retendue"));
        }

        // Message Msg.Vehicle au format de VehicleSync.Send : etat 2 = la voiture avance chez nous sans conducteur
        // assis (pose, vitesses ; regime, accelerateur, braquage nuls, pas de chaleur ni de sons), 0 = rendue.
        static void SendCar(int idx, int mode, bool reliable)
        {
            Rigidbody b = VehicleSync.CarBody(idx);
            if (b == null) return;
            var w = new NetWriter(Msg.Vehicle).U8(Session.LocalId).U8(idx).U8(mode)
                .Vec(b.position).Quat(b.rotation).Vec(b.velocity).Vec(b.angularVelocity);
            if (mode != 0) w.F32(0f).F32(0f).F32(0f).F32(float.NaN).U16(0);
            Session.SendAll(w, reliable);
        }

        // ---------------------------------------------------------------- utilitaires
        static int CarOf(Rigidbody b)
        {
            if (b == null) return -1;
            return VehicleSync.CarIndex(b.transform.root.GetComponent<Rigidbody>());
        }

        static string CarName(int idx)
        {
            Rigidbody b = VehicleSync.CarBody(idx);
            return b != null ? b.name : "#" + idx;
        }

        static string Name(int id)
        {
            PlayerInfo pi;
            return Session.Players.TryGetValue(id, out pi) ? pi.Name : "joueur #" + id;
        }

        static string Settings(Rope r)
        {
            return "longueur " + r.MaxDist.ToString("F2") + " m, ressort " + r.Spring.ToString("F0") + ", amortisseur " + r.Damper.ToString("F2")
                   + ", casse " + r.BreakForce.ToString("F0") + (r.Car1 >= 0 ? ", " + CarName(r.Car1) : "") + (r.Car2 >= 0 ? " / " + CarName(r.Car2) : "");
        }

        // ---------------------------------------------------------------- essais
        // [Test] Autotest=remorque : l'hote amene [Test] RemorqueAutre (GIFU par defaut) 2 m derriere [Test]
        // RemorqueVoiture (SORBET) a 25 s, va a cote de son crochet avant a 29 s, clique le crochet arriere de la
        // SORBET a 30 s puis le crochet avant de l'autre a 31 s (etats 'State 3' des automates 'Logic', comme le
        // bouton de la souris) et retire la corde a 60 s ('Remove rope'). Chacun note toutes les 2 s, de 26 a
        // 75 s, les cordes vues chez lui : joint present ou non, distance des crochets.
        static int testStep;
        static float testLog;
        static Transform testH1, testH2;

        public static void Test(string mode, float t)
        {
            if (mode != "remorque") return;
            if (t >= 26f && t < 76f && t >= testLog) { testLog = t + 2f; Log.Info("autotest : remorque t=" + t.ToString("F0") + " : " + State()); }
            if (!Session.IsHost) return;
            if (testStep == 0 && t >= 25f) { testStep = 1; Log.Info("autotest : remorque : " + TestPlace()); }
            if (testStep == 1 && t >= 29f) { testStep = 2; Log.Info("autotest : remorque : " + TestWalk()); }
            if (testStep == 2 && t >= 30f) { testStep = 3; Log.Info("autotest : remorque : clic " + TestClick(testH1, "State 3")); }
            if (testStep == 3 && t >= 31f) { testStep = 4; Log.Info("autotest : remorque : clic " + TestClick(testH2, "State 3")); }
            if (testStep == 4 && t >= 60f) { testStep = 5; Log.Info("autotest : remorque : retrait " + TestClick(testH1, "Remove rope")); }
        }

        static Rigidbody TestCar(string prefix)
        {
            for (int i = 0; i < VehicleSync.Count; i++)
            {
                Rigidbody b = VehicleSync.CarBody(i);
                if (b != null && b.name.StartsWith(prefix)) return b;
            }
            return null;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.normalized; }

        static string TestPlace()
        {
            Rigidbody a = TestCar(Config.Get("Test", "RemorqueVoiture", "SORBET")), b = TestCar(Config.Get("Test", "RemorqueAutre", "GIFU"));
            if (a == null || b == null) return "voitures introuvables (" + VehicleSync.Count + " suivies)";
            Transform aF = a.transform.Find("HookFront"), aR = a.transform.Find("HookRear"), bF = b.transform.Find("HookFront"), bR = b.transform.Find("HookRear");
            if (aF == null || aR == null || bF == null || bR == null) return "crochets introuvables";
            Vector3 da = Flat(aF.position - aR.position), db = Flat(bF.position - bR.position);
            float yaw = (Mathf.Atan2(da.x, da.z) - Mathf.Atan2(db.x, db.z)) * Mathf.Rad2Deg;
            b.transform.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * b.transform.rotation;
            Vector3 target = aR.position - da * 2f;
            b.transform.position += target - bF.position + Vector3.up * 0.3f;
            b.velocity = Vector3.zero;
            b.angularVelocity = Vector3.zero;
            testH1 = aR; testH2 = bF;
            return b.name + " amenee derriere " + a.name + ", crochets a " + Vector3.Distance(aR.position, bF.position).ToString("F2") + " m";
        }

        static string TestWalk()
        {
            if (testH1 == null || testH2 == null) return "pas de crochets";
            GameObject p = GameObject.Find("PLAYER");
            var cc = p != null ? p.GetComponent<CharacterController>() : null;
            if (cc == null) return "pas de joueur";
            Vector3 side = Vector3.Cross(Vector3.up, Flat(testH2.position - testH1.position));
            cc.enabled = false;
            p.transform.position = testH2.position + side * 1f + Vector3.up * 0.5f;
            cc.enabled = true;
            return "joueur a cote de " + Recon.Path(testH2) + ", a " + Vector3.Distance(p.transform.position, testH1.position).ToString("F2") + " m du 1er crochet";
        }

        static string TestClick(Transform hook, string state)
        {
            if (hook == null) return "pas de crochet";
            PlayMakerFSM f = Game.FsmOn(hook.gameObject, "Logic");
            if (f == null) return "pas d'automate sur " + hook.name;
            string before = f.ActiveStateName;
            Game.SetState(f, state);
            return Recon.Path(hook) + " " + before + " -> " + f.ActiveStateName + ", corde " + (ropeFsm != null ? ropeFsm.ActiveStateName : "?")
                   + (rAttached != null && rAttached.Value ? " (accrochee)" : "");
        }

        static string State()
        {
            var sb = new StringBuilder();
            if (local != null) Describe(sb, local);
            foreach (Rope m in mirrors.Values) { if (sb.Length > 0) sb.Append(" | "); Describe(sb, m); }
            if (sb.Length == 0) sb.Append("aucune corde");
            if (ropeFsm != null) sb.Append(" ; corde locale ").Append(ropeGo.activeSelf ? "sortie" : "rangee").Append(", etat ").Append(ropeFsm.ActiveStateName)
                                   .Append(rAttached.Value ? ", Attached" : "");
            return sb.ToString();
        }

        static void Describe(StringBuilder sb, Rope r)
        {
            sb.Append(r.Mirror ? "corde de " + Name(r.Owner) : "notre corde").Append(' ').Append(r.Path1).Append(" <-> ").Append(r.Path2);
            sb.Append(" : joint ").Append(r.Joint != null ? r.Joint.GetType().Name : "absent");
            if (r.Hook1 != null && r.Hook2 != null)
                sb.Append(", crochets a ").Append(Vector3.Distance(r.Hook1.position, r.Hook2.position).ToString("F2")).Append(" m (longueur ").Append(r.MaxDist.ToString("F2")).Append(')');
            if (r.Mirror) sb.Append(r.Visual != null ? ", visible" : ", invisible");
            if (r.Body1 != null) sb.Append(", corps ").Append(r.Body1.isKinematic ? "cinematique" : "physique");
            if (r.Body2 != null) sb.Append('/').Append(r.Body2.isKinematic ? "cinematique" : "physique");
            if (r.Carry >= 0) sb.Append(", on tire ").Append(CarName(r.Carry));
            if (r.Soft) sb.Append(", detendue");
        }
    }
}
