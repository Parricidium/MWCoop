using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Suski (My Summer Car, dossier #65 suite, 11/11) : passager de la KYLAJANI des amis (NPC_CARS/Amikset/KYLAJANI). A l'accident
    // (CrashEvent::Crash, attache DeathForce cassee), son pantin NPC_CARS/Amikset/KYLAJANI/CrashEvent/RagDoll (11 corps relies par des
    // CharacterJoint) sort de la voiture ; il demande de l'aide (RagDoll::Talk -> PLAYERHELP a JOBS/Suski/SuskiStages::Logic), on le porte
    // jusqu'a un lit (Beds, 0,6 m), le joueur dort : la note de remerciement apparait sur le lit (SuskiStage 2 -> 3 -> 4). Au chargement,
    // 'Activate rag' le rattache a JOBS/Suski/SuskiStages.
    // Avant : rien de ceci n'etait partage. L'accident n'arrive que chez l'hote (Traffic coupe celui de la copie chez l'invite) et le pantin,
    // sous NPC_CARS puis a la racine de la scene, n'etait suivi par personne (essai su1 : pantin libre chez l'hote, toujours range dans la
    // voiture chez l'invite) -- « seul le conducteur l'a » (dossier #65, joueur 29.30).
    //  - Etat decide par l'hote (fiable, a chaque changement et toutes les 2 s, ce qui sert aussi a l'invite arrive en retard) : pantin sorti
    //    de la voiture, pantin parti (l'histoire l'a detruit ou eteint), qui le mene, note posee sur le lit (et sa pose), Suski endormi, etape.
    //  - Un seul joueur mene le pantin (physique chez lui) : celui qui le tient en main ; sinon, pantin pose dans une voiture, celui qui fait
    //    autorite sur cette voiture (son conducteur) ; sinon le dernier qui l'a mene (l'hote au debut). Les autres le portent d'un bloc :
    //    ses 11 corps cinematiques suivent la pose du meneur (10 fois par seconde, interpolee 0,15 s en arriere, dans le repere de la voiture
    //    s'il y est pose) -- garde-fous du crash de Донской (d78d97ee) : jamais de membre physique tire par un membre cinematique, pose non
    //    finie ou au-dela de 1e5 m ni envoyee ni appliquee, grand saut pose d'un coup (cinematique : sans elan) ; rendu a la physique a la
    //    vitesse du point de la voiture ou il est (ou immobile), jamais a la vitesse d'un saut.
    //  - Histoire : chez l'hote seulement. L'invite coupe sa SuskiStages::Logic une fois chargee (elle aurait fini l'histoire de son cote,
    //    eteint le pantin, compte une autre etape) ; il recoit l'etape, Suski endormi (pour que Talk le remercie), la note et sa pose ; il
    //    lit la note (NOTEREAD) : l'hote la lit pour lui.
    // [Test] Suski=0 : comme avant (rien de partage).
    public static class Suski
    {
        const string RagPath = "NPC_CARS/Amikset/KYLAJANI/CrashEvent/RagDoll", CarPath = "NPC_CARS/Amikset/KYLAJANI", StagesPath = "JOBS/Suski/SuskiStages",
                     NotePath = "JOBS/Suski/SuskiStages/Note/note(xxxxx)";
        // Msg.Suski : [U8 sorte] puis
        //  0 etat (hote -> invites, fiable) : [U8 drapeaux 1 sorti, 2 parti, 4 note posee, 8 endormi][U8 meneur][I32 etape] (+ [Vec][Quat] note posee)
        //  1 pose (meneur -> autres, non fiable ; d'un invite : a l'hote qui relaie) : [U8 meneur][U8 voiture | 255][U8 n] puis n x [Vec][Quat]
        //  2 tenu (invite -> hote, fiable) : [U8 1 | 0]
        //  3 note lue (invite -> hote, fiable)
        const int K_STATE = 0, K_POSE = 1, K_HOLD = 2, K_NOTE = 3;
        const float SendEvery = 0.1f, StateEvery = 2f, Delay = 0.15f, MaxCoord = 1e5f, JumpSnap = 30f;
        static bool On { get { return Game.Msc && Config.GetInt("Test", "Suski", 1) != 0; } }

        static Transform rag, car, note;
        static Rigidbody[] bodies;
        static PlayMakerFSM stages, talk, noteUse, hand;
        static bool stagesMuted, following, heldHere, sentHeld, noteSent, noteHooked;
        static int owner, holder = -1, lastOwnerLog = -2, stageSeen;
        static bool sOut, sGone, sNote, sSleeps;   // dernier etat (hote : calcule ; invite : recu)
        static int sStage;
        static Vector3 sNotePos; static Quaternion sNoteRot;
        static float nextState, nextSend, nextFind, nextOwner, inGameAt, noteHookAt;
        static int logs; static float errAt;

        class Snap { public float T; public int Car = -1; public Vector3[] P; public Quaternion[] R; }
        static Snap s0, s1;   // les deux dernieres poses recues (heure de reception ici)

        public static void OnLevelLoaded()
        {
            rag = car = note = null; bodies = null; stages = talk = noteUse = hand = null;
            stagesMuted = following = heldHere = sentHeld = noteSent = noteHooked = false;
            owner = 0; holder = -1; lastOwnerLog = -2; stageSeen = 0;
            sOut = sGone = sNote = sSleeps = false; sStage = 0;
            nextState = nextSend = nextFind = nextOwner = noteHookAt = 0f; inGameAt = -1f; logs = 0;
            s0 = s1 = null;
            testStep = 0; testNext = 0f; lastShot = 0f; lit = false; testHold = false; passDone = false; approched = false; routeDone = false;
        }

        static void FindRefs()
        {
            if (rag == null) { GameObject g = Game.FindAny(RagPath); if (g != null) { rag = g.transform; bodies = rag.GetComponentsInChildren<Rigidbody>(true); } }
            if (car == null) { GameObject g = Game.FindAny(CarPath); if (g != null) car = g.transform; }
            if (stages == null)
            {
                GameObject g = Game.FindAny(StagesPath);
                if (g != null) { stages = Game.FsmOn(g, "Logic"); if (stages != null) Replay.Claim(stages, "suski"); }   // (pas de rejeu WorldFsms : a ce module)
            }
            if (talk == null && rag != null) talk = Game.FsmOn(rag.gameObject, "Talk");
            if (note == null) { GameObject g = Game.FindAny(NotePath); if (g != null) { note = g.transform; noteUse = Game.FsmOn(g, "Use"); } }
            if (hand == null) { GameObject h = Game.PlayerPart("Pivot/AnimPivot/Camera/FPSCamera/1Hand_Assemble/Hand"); if (h != null) hand = Game.FsmOn(h, "PickUp"); }
        }

        // Corps de Suski (Props ne les suit pas : ce module les mene).
        public static bool Owns(Rigidbody rb)
        {
            if (!On || rb == null || rag == null) return false;
            return rb.transform == rag || rb.transform.IsChildOf(rag);
        }

        static bool Out { get { return rag != null && rag.gameObject.activeSelf && (car == null || !rag.IsChildOf(car)); } }   // sorti de la voiture, la
        static Rigidbody Pelvis { get { return bodies != null && bodies.Length > 0 ? bodies[0] : null; } }
        static string Who(int id) { return id == Session.LocalId ? "moi" : "#" + id; }

        public static void Update()
        {
            if (!On || !Session.Active || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if (inGameAt < 0f) inGameAt = now;
            if (now >= nextFind) { nextFind = now + 2f; FindRefs(); }
            if (bodies == null) return;
            // (chaque partie a part : une erreur dans l'une n'empeche jamais le pantin de suivre)
            try { DetectHold(); if (Session.IsHost) HostUpdate(now); else GuestUpdate(now); }
            catch (System.Exception e) { if (now >= errAt) { errAt = now + 10f; Log.Error("suski : " + e); } }
            ApplyOwner(now);
            if (owner == Session.LocalId && sOut && !sGone && rag != null && now >= nextSend) { nextSend = now + SendEvery; SendPose(); }
            if (following) Follow(now);
        }

        // ---------------------------------------------------------------- tenu en main ici
        static void DetectHold()
        {
            bool h = false;
            if (hand != null && rag != null && rag.gameObject.activeInHierarchy)
            {
                GameObject go = hand.FsmVariables.GetFsmGameObject("PickedObject").Value;
                if (go != null && hand.ActiveStateName != "Look for object" && (go.transform == rag || go.transform.IsChildOf(rag))) h = true;
            }
            if (testHold) h = true;
            if (h == heldHere) return;
            heldHere = h;
            if (logs++ < 40) Log.Info("suski : pantin " + (h ? "pris en main ici" : "lache ici"));
            if (Session.IsHost) { if (h) holder = 0; else if (holder == 0) holder = -1; nextOwner = 0f; }
            else Session.SendToHost(new NetWriter(Msg.Suski).U8(K_HOLD).U8(h ? 1 : 0), true);
        }

        // ---------------------------------------------------------------- hote : etat et meneur
        static void HostUpdate(float now)
        {
            bool outNow = Out, gone = sOut && (rag == null || !rag.gameObject.activeSelf);
            bool noteOn = note != null && note.gameObject.activeInHierarchy;
            FsmInt st = stages != null ? stages.FsmVariables.FindFsmInt("SuskiStage") : null;
            FsmBool sl = stages != null ? stages.FsmVariables.FindFsmBool("SuskiSleeps") : null;
            bool changed = outNow != sOut && outNow || gone != sGone || noteOn != sNote || (sl != null && sl.Value != sSleeps) || (st != null && st.Value != sStage);
            if (outNow) sOut = true;    // (sorti une fois : le reste vaut)
            sGone = gone; sNote = noteOn; sSleeps = sl != null && sl.Value; sStage = st != null ? st.Value : 0;
            if (noteOn) { sNotePos = note.position; sNoteRot = note.rotation; }
            if (sOut && !sGone && now >= nextOwner)
            {
                nextOwner = now + 0.5f;
                int want = owner;
                if (holder >= 0 && Session.Players.ContainsKey(holder)) want = holder;
                else
                {
                    holder = -1;
                    Rigidbody c = Pelvis != null ? VehicleSync.CarUnder(Pelvis) : null;
                    int ci = VehicleSync.CarIndex(c);
                    if (ci >= 0) { int a = VehicleSync.Authority(ci); if (a >= 0 && Session.Players.ContainsKey(a)) want = a; }
                }
                if (!Session.Players.ContainsKey(want)) want = 0;
                if (want != owner) { owner = want; changed = true; }
            }
            if (changed || now >= nextState) { nextState = now + StateEvery; SendState(); }
        }

        static void SendState()
        {
            if (Session.RemoteCount == 0) return;
            int f = (sOut ? 1 : 0) | (sGone ? 2 : 0) | (sNote ? 4 : 0) | (sSleeps ? 8 : 0);
            var w = new NetWriter(Msg.Suski).U8(K_STATE).U8(f).U8(owner).I32(sStage);
            if (sNote) w.Vec(sNotePos).Quat(sNoteRot);
            Session.Broadcast(w, true);
        }

        // ---------------------------------------------------------------- invite : histoire coupee, etat applique
        static void GuestUpdate(float now)
        {
            // SuskiStages::Logic coupee une fois son chargement passe (Load / Activate rag : pantin rattache comme chez l'hote).
            if (!stagesMuted && stages != null)
            {
                string a = stages.ActiveStateName;
                if (now - inGameAt > 20f || a == "Ragdoll active" || a == "Fail" || a == "Note active" || a == "State 2" || a == "The End" || a == "The End 2" || a == "Angry" || a == "State 1")
                {
                    stagesMuted = true; stages.enabled = false;
                    Log.Info("suski : SuskiStages::Logic coupee ici (etait '" + a + "') : l'histoire de Suski est menee par l'hote");
                }
            }
            // Note lue ici : l'hote la lit (son automate Use envoie NOTEREAD a la SuskiStages coupee d'ici). Crochet en tete de 'Wait button'
            // (survol : l'etat peut ne durer qu'une image -- un releve a chaque image le manquait).
            // Seulement la note allumee (automate initialise ; eteinte, la lecture de ses actions levait une exception a chaque image et coupait
            // la suite -- le pantin ne suivait plus, essai su3 du 11/11). Sans Claim : WorldFsms peut deja suivre cet automate ; un NOTEREAD
            // de plus chez l'hote ne change rien. Echec : nouvel essai 2 s plus tard, jamais a chaque image.
            if (!noteHooked && now >= noteHookAt && noteUse != null && noteUse.gameObject.activeInHierarchy && noteUse.Fsm != null)
            {
                noteHookAt = now + 2f;
                try
                {
                    foreach (FsmState st in noteUse.Fsm.States)
                        if (st.Name == "Wait button") { FsmHooks.Prepend(st, new NoteHook()); noteHooked = true; Log.Info("suski : lecture de la note suivie ici"); break; }
                }
                catch (System.Exception e) { if (logs++ < 40) Log.Warn("suski : note pas encore prete (" + e.GetType().Name + "), nouvel essai dans 2 s"); }
            }
        }

        class NoteHook : ModHook
        {
            public override string Module { get { return "suski"; } }
            public override void OnEnter()
            {
                try { NoteRead(); } catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        static void NoteRead()
        {
            if (Session.IsHost || !sNote || noteSent) return;
            noteSent = true;
            Session.SendToHost(new NetWriter(Msg.Suski).U8(K_NOTE), true);
            Log.Info("suski : note lue ici, dit a l'hote");
        }

        static void ApplyState(int f, int own, int stage, Vector3 np, Quaternion nr, bool hasNote)
        {
            bool outNow = (f & 1) != 0, gone = (f & 2) != 0, noteOn = (f & 4) != 0, sleeps = (f & 8) != 0;
            FindRefs();
            if (outNow && !gone && rag != null && !Out) ReplayCrash();
            if (gone && rag != null && rag.gameObject.activeSelf) { following = false; rag.gameObject.SetActive(false); Log.Info("suski : pantin parti chez l'hote (histoire) : eteint ici"); }
            if (note != null)
            {
                Transform nt = note.parent;   // (Note, eteint au depart)
                if (noteOn && hasNote)
                {
                    if (nt != null && !nt.gameObject.activeSelf) nt.gameObject.SetActive(true);
                    if (!note.gameObject.activeSelf) note.gameObject.SetActive(true);
                    Rigidbody nb = note.GetComponent<Rigidbody>();
                    if (nb != null) nb.isKinematic = true;
                    note.position = np; note.rotation = nr;
                    if (!sNote) Log.Info("suski : note posee chez l'hote en " + np.ToString("F2") + " : posee ici");
                }
                else if (!noteOn && sNote && note.gameObject.activeSelf) note.gameObject.SetActive(false);
            }
            if (stages != null)
            {
                FsmInt st = stages.FsmVariables.FindFsmInt("SuskiStage"); if (st != null) st.Value = stage;
                FsmBool sl = stages.FsmVariables.FindFsmBool("SuskiSleeps"); if (sl != null) sl.Value = sleeps;   // (Talk : « Thank you so much »)
            }
            if (stage != sStage && logs++ < 40) Log.Info("suski : etape " + stage + " chez l'hote");
            sOut = outNow; sGone = gone; sNote = noteOn; sSleeps = sleeps; sStage = stage;
            if (!noteOn) noteSent = false;
            owner = own;
        }

        // Accident vu chez l'hote, pas ici (Traffic coupe celui de la copie) : ce que CrashEvent::Crash 'Ragdoll' + 'Free ragdoll' montrent --
        // pantin allume, sorti de la voiture, detache ; passager assis et conducteur eteints, pare-brise casse. Le reste (voiture arretee,
        // feux, sang) suit la voiture de l'hote ou ne compte pas.
        static void ReplayCrash()
        {
            rag.gameObject.SetActive(true);
            FixedJoint fj = rag.GetComponent<FixedJoint>();
            if (fj != null) Object.Destroy(fj);
            rag.SetParent(null, true);
            if (car != null)
            {
                Transform p = car.Find("LOD/Passenger"); if (p != null) p.gameObject.SetActive(false);
                Transform d = car.Find("Driver"); if (d != null) d.gameObject.SetActive(false);
                Transform b = car.Find("LOD/brokenx"); if (b != null) b.gameObject.SetActive(true);
            }
            foreach (Rigidbody rb in bodies) if (rb != null) { rb.isKinematic = true; }
            following = true; s0 = s1 = null;
            Log.Info("suski : accident de la KYLAJANI chez l'hote : Suski sorti ici aussi, porte d'un bloc (" + bodies.Length + " corps)");
        }

        // ---------------------------------------------------------------- meneur : physique ici, ou porte d'un bloc
        static void ApplyOwner(float now)
        {
            if (rag == null || !sOut || sGone || !rag.gameObject.activeInHierarchy) return;
            bool mine = owner == Session.LocalId;
            if (mine && following)
            {
                following = false;
                // Rendu a la physique a la vitesse du point de la voiture ou il est pose (sinon immobile) : jamais a la vitesse d'un saut.
                Rigidbody c = Pelvis != null ? VehicleSync.CarUnder(Pelvis) : null;
                foreach (Rigidbody rb in bodies)
                {
                    if (rb == null) continue;
                    rb.isKinematic = false;
                    rb.velocity = c != null ? c.GetPointVelocity(rb.position) : Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                s0 = s1 = null;
            }
            else if (!mine && !following)
            {
                following = true; s0 = s1 = null;
                foreach (Rigidbody rb in bodies) if (rb != null) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; rb.isKinematic = true; }
            }
            if (owner != lastOwnerLog) { lastOwnerLog = owner; if (logs++ < 40) Log.Info("suski : pantin mene par " + Who(owner) + (mine ? " (physique ici)" : " (porte d'un bloc ici)")); }
        }

        static void SendPose()
        {
            Rigidbody c = VehicleSync.CarUnder(Pelvis);
            int ci = VehicleSync.CarIndex(c);
            if (ci < 0 || ci > 254) { ci = -1; c = null; }
            Quaternion inv = c != null ? Quaternion.Inverse(c.rotation) : Quaternion.identity;
            var w = new NetWriter(Msg.Suski).U8(K_POSE).U8(Session.LocalId).U8(ci < 0 ? 255 : ci).U8(bodies.Length);
            foreach (Rigidbody rb in bodies)
            {
                Vector3 p = rb != null ? rb.position : Vector3.zero; Quaternion q = rb != null ? rb.rotation : Quaternion.identity;
                if (!Sane(p) || !Sane(q)) return;   // (pose non finie ou aberrante : jamais envoyee -- NanGuard s'en charge ici)
                if (c != null) { p = inv * (p - c.position); q = inv * q; }
                w.Vec(p).Quat(q);
            }
            if (Session.IsHost) Session.Broadcast(w, false); else Session.SendToHost(w, false);
        }

        static bool Sane(Vector3 v) { return !float.IsNaN(v.x + v.y + v.z) && !float.IsInfinity(v.x + v.y + v.z) && Mathf.Abs(v.x) < MaxCoord && Mathf.Abs(v.y) < MaxCoord && Mathf.Abs(v.z) < MaxCoord; }
        static bool Sane(Quaternion q) { float s = q.x + q.y + q.z + q.w; return !float.IsNaN(s) && !float.IsInfinity(s) && (q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w) > 0.5f; }

        static void Follow(float now)
        {
            if (s1 == null || rag == null || !rag.gameObject.activeInHierarchy) return;
            float rt = now - Delay;
            Snap a = s1, b = null; float u = 0f;
            if (s0 != null && rt < s1.T && rt > s0.T && s0.Car == s1.Car) { a = s0; b = s1; u = (rt - s0.T) / Mathf.Max(0.001f, s1.T - s0.T); }
            Rigidbody c = a.Car >= 0 ? VehicleSync.CarBody(a.Car) : null;
            if (a.Car >= 0 && c == null) return;   // voiture inconnue ici : le pantin reste ou il est
            for (int i = 0; i < bodies.Length && i < a.P.Length; i++)
            {
                Rigidbody rb = bodies[i];
                if (rb == null) continue;
                Vector3 p = b != null ? Vector3.Lerp(a.P[i], b.P[i], u) : a.P[i];
                Quaternion q = b != null ? Quaternion.Slerp(a.R[i], b.R[i], u) : a.R[i];
                if (c != null) { p = c.transform.TransformPoint(p); q = c.transform.rotation * q; }
                if (!rb.isKinematic) rb.isKinematic = true;
                rb.transform.position = p; rb.transform.rotation = q;
            }
        }

        // ---------------------------------------------------------------- messages
        public static void OnMessage(Peer from, NetReader r)
        {
            if (!On) return;
            int kind = r.U8();
            switch (kind)
            {
                case K_STATE:
                {
                    if (Session.IsHost) return;
                    int f = r.U8(), own = r.U8(), stage = r.I32();
                    Vector3 np = Vector3.zero; Quaternion nr = Quaternion.identity; bool hn = false;
                    if ((f & 4) != 0 && r.More) { np = r.Vec(); nr = r.Quat(); hn = Sane(np); }
                    ApplyState(f, own, stage, np, nr, hn);
                    break;
                }
                case K_HOLD:
                {
                    if (!Session.IsHost) return;
                    bool h = r.U8() != 0;
                    if (h) holder = from.Id; else if (holder == from.Id) holder = -1;
                    nextOwner = 0f;
                    if (logs++ < 40) Log.Info("suski : pantin " + (h ? "pris en main" : "lache") + " par #" + from.Id);
                    break;
                }
                case K_NOTE:
                {
                    if (!Session.IsHost || stages == null) return;
                    stages.SendEvent("NOTEREAD");
                    Log.Info("suski : note lue par #" + from.Id + " : lue ici (NOTEREAD)");
                    break;
                }
                case K_POSE:
                {
                    int sender = r.U8();
                    if (Session.IsHost) { sender = from.Id; Session.Broadcast(new NetWriter(Msg.Suski).U8(K_POSE).U8(sender).Raw(r.Rest()), false, sender); }
                    int ci = r.U8(), n = r.U8();
                    if (sender != owner || bodies == null || n != bodies.Length) return;   // (pas ou plus le meneur : paquet en retard)
                    var s = new Snap { T = Time.realtimeSinceStartup, Car = ci == 255 ? -1 : ci, P = new Vector3[n], R = new Quaternion[n] };
                    for (int i = 0; i < n; i++) { s.P[i] = r.Vec(); s.R[i] = r.Quat(); if (!Sane(s.P[i]) || !Sane(s.R[i])) { if (logs++ < 40) Log.Warn("suski : pose aberrante recue de #" + sender + " : ignoree"); return; } }
                    // Grand saut (teleporte, ou change de repere) : pose d'un coup, sans glisser (cinematique : sans elan).
                    if (s1 != null && (s1.Car != s.Car || (s1.P[0] - s.P[0]).sqrMagnitude > JumpSnap * JumpSnap)) s0 = null; else s0 = s1;
                    s1 = s;
                    break;
                }
            }
        }

        // ---------------------------------------------------------------- essais
        static int testStep; static float testNext, lastShot; static bool lit, testHold, passDone, approched, routeDone;
        static Vector3 testHoldAt;

        static string Probe()
        {
            FindRefs();
            var sb = new System.Text.StringBuilder();
            sb.Append("KYLAJANI ").Append(car == null ? "introuvable" : (car.gameObject.activeInHierarchy ? "active" : "inactive") + " en " + car.position.ToString("F1"));
            if (rag == null) sb.Append(" ; pantin introuvable");
            else
            {
                sb.Append(" ; pantin ").Append(rag.gameObject.activeInHierarchy ? "actif" : "inactif").Append(" sous ").Append(rag.parent != null ? Recon.Path(rag.parent) : "(racine)");
                Rigidbody pel = Pelvis;
                sb.Append(", bassin ").Append((pel != null ? pel.position : rag.position).ToString("F2"));
                int kin = 0, n = 0; float vmax = 0f; bool bad = false;
                foreach (Rigidbody rb in rag.GetComponentsInChildren<Rigidbody>(true)) { n++; if (rb.isKinematic) kin++; vmax = Mathf.Max(vmax, rb.velocity.magnitude); if (!Sane(rb.position)) bad = true; }
                // ecart max des membres au bassin (pantin disloque ?)
                float spread = 0f;
                if (pel != null) foreach (Rigidbody rb in bodies) if (rb != null) spread = Mathf.Max(spread, (rb.position - pel.position).magnitude);
                sb.Append(", corps ").Append(n).Append(" dont ").Append(kin).Append(" cinematiques, vitesse max ").Append(vmax.ToString("F1")).Append(", ecart max ").Append(spread.ToString("F2")).Append(" m");
                if (bad) sb.Append(", POSE NON FINIE");
                Rigidbody c = pel != null ? VehicleSync.CarUnder(pel) : null;
                if (c != null) sb.Append(", dans ").Append(c.name).Append(" (vitesse relative ").Append((pel.velocity - c.GetPointVelocity(pel.position)).magnitude.ToString("F1")).Append(")");
                Transform cam = PlayerSync.LocalCamera;
                if (cam != null) sb.Append(", a ").Append((cam.position - rag.position).magnitude.ToString("F1")).Append(" m");
            }
            sb.Append(" ; mene par ").Append(Who(owner)).Append(following ? ", porte ici" : "").Append(heldHere ? ", tenu ici" : "");
            sb.Append(" ; Talk ").Append(talk == null ? "-" : (talk.enabled ? "" : "(coupe) ") + talk.ActiveStateName);
            if (stages != null)
            {
                FsmInt st = stages.FsmVariables.FindFsmInt("SuskiStage");
                FsmBool sl = stages.FsmVariables.FindFsmBool("SuskiSleeps");
                sb.Append(" ; Stages ").Append(stages.enabled ? "" : "(coupe) ").Append(stages.ActiveStateName).Append(", SuskiStage ").Append(st != null ? st.Value.ToString() : "?").Append(", dort ").Append(sl != null && sl.Value);
            }
            sb.Append(" ; note ").Append(note != null && note.gameObject.activeInHierarchy ? "posee en " + note.position.ToString("F2") : "absente");
            return sb.ToString();
        }

        static void LookAt(Vector3 target)
        {
            GameObject p = Game.PlayerGo;
            Transform cam = p != null ? p.transform.Find("Pivot/AnimPivot/Camera/FPSCamera") : null;
            if (cam == null) return;
            foreach (Behaviour b in p.GetComponents<Behaviour>()) if (b.GetType().Name.Contains("MouseLook")) b.enabled = false;
            foreach (Behaviour b in cam.GetComponents<Behaviour>()) if (b.GetType().Name.Contains("MouseLook")) b.enabled = false;
            Vector3 to = target - cam.position;
            p.transform.rotation = Quaternion.Euler(0, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, 0);
            cam.localRotation = Quaternion.Euler(-Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg, 0, 0);
        }

        static string Role { get { return Session.IsHost ? "hote" : "invite"; } }

        // Essais : le pantin deplace d'un bloc (sa racine porte les autres corps : ne deplacer qu'elle), immobile ou a la vitesse de 'car'.
        static void MoveRag(Vector3 d, Rigidbody car)
        {
            rag.position += d;
            foreach (Rigidbody rb in bodies)
            {
                if (rb == null) continue;
                rb.position = rb.transform.position; rb.rotation = rb.transform.rotation;
                rb.velocity = car != null ? car.GetPointVelocity(rb.position) : Vector3.zero; rb.angularVelocity = Vector3.zero;
            }
        }

        // ([Test] SuskiVoiture=t,car[,x,y,z] : place du pantin dans le repere de la voiture -- banquette de la SATSUMA par defaut)
        static Vector3 SuskiPlace(string[] vr)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture; float x, y, z;
            if (vr.Length >= 5 && float.TryParse(vr[2], System.Globalization.NumberStyles.Float, ci, out x) && float.TryParse(vr[3], System.Globalization.NumberStyles.Float, ci, out y) && float.TryParse(vr[4], System.Globalization.NumberStyles.Float, ci, out z)) return new Vector3(x, y, z);
            return new Vector3(0f, 1.0f, -0.55f);
        }

        static float T(string key, float def) { return Config.GetFloat("Test", key, def); }
        static Vector3 V(string key) { string[] c = Config.Get("Test", key, "").Split(','); var ci = System.Globalization.CultureInfo.InvariantCulture; return c.Length >= 3 ? new Vector3(float.Parse(c[0], ci), float.Parse(c[1], ci), float.Parse(c[2], ci)) : Vector3.zero; }

        // Essais : physique ici seulement (meneur). Bassin tire vers 'to' comme la main du jeu (ressort), les membres suivent par leurs joints.
        public static void FixedUpdate()
        {
            if (!testHold || bodies == null || Pelvis == null || Pelvis.isKinematic) return;
            Rigidbody pel = Pelvis;
            Vector3 v = (testHoldAt - pel.position) * 8f;
            if (v.magnitude > 12f) v = v.normalized * 12f;
            pel.velocity = v;
        }

        // [Test] Autotest=suski, 2 joueurs. Accident : SuskiAllumer (l'hote coupe NPC_CARS::Amis Setup et allume la KYLAJANI), SuskiPres (chacun
        // a SuskiEcart m a droite de SA KYLAJANI), SuskiAccident (l'hote casse DeathForce). Puis, selon les cles presentes :
        //  SuskiPrise=t,qui : 'qui' (hote|invite) tient le pantin de t a t+SuskiTenue (12 s), bassin tire a 1,2 m devant sa camera ;
        //   SuskiLacherHaut=1 : tenu a 2,5 m au-dessus de lui, puis lache (chute) ; SuskiSecousse=1 : tire a 12 m/s a gauche puis lache.
        //  SuskiVoiture=t,car : a t, le meneur pose le pantin sur la banquette arriere de 'car' (SATSUMA) ; SuskiConduite=t1,t2,qui : 'qui'
        //   s'assied au volant a t1-3 et roule (gaz) jusqu'a t2, puis freine fort 3 s (SuskiFreinage=1).
        //  SuskiLit=t : a t, le meneur pose le pantin sur Sleep1 ; SuskiDort=t : l'hote met PlayerSleeps a vrai a t (5 s) ;
        //   SuskiLireNote=t : l'invite se pose devant la note et la regarde a t.
        // Chacun note toutes les 2 s de 20 a SuskiFin ; captures aux instants SuskiCaptures, la camera sur le pantin (ou la note).
        public static void Test(string mode, float t)
        {
            if (mode != "suski" || !Game.Msc) return;
            FindRefs();
            float on = T("SuskiAllumer", 0f);
            if (Session.IsHost && on > 0f && testStep == 0 && !lit && t > on && car != null)
            {
                lit = true;
                GameObject root = Game.FindAny("NPC_CARS");
                PlayMakerFSM setup = root != null ? Game.FsmOn(root, "Amis Setup") : null;
                if (setup != null) setup.enabled = false;
                var off = new List<Transform>();
                for (Transform x = car; x != null; x = x.parent) if (!x.gameObject.activeSelf) off.Add(x);
                for (int i = off.Count - 1; i >= 0; i--) off[i].gameObject.SetActive(true);
                Log.Info("autotest suski (hote) : Amis Setup " + (setup != null ? "coupe" : "introuvable") + ", allume " + off.Count + " objet(s) ; " + Probe());
            }
            if (testStep == 0 && t > T("SuskiPres", 25f))
            {
                testStep = 1;
                if (car != null) { Vector3 at = car.position + car.right * T("SuskiEcart", 10f) + Vector3.up * 1.5f; Autotest.PutPlayer(at); Log.Info("autotest suski (" + Role + ") : pose a cote de la KYLAJANI en " + at.ToString("F1")); }
            }
            if (Session.IsHost && testStep == 1 && t > T("SuskiAccident", 32f))
            {
                testStep = 2;
                Transform df = car != null ? car.Find("CrashEvent/DeathForce") : null;
                FixedJoint j = df != null ? df.GetComponent<FixedJoint>() : null;
                if (j != null) Object.Destroy(j);
                Log.Info("autotest suski (hote) : attache DeathForce " + (j != null ? "cassee" : "introuvable"));
            }
            Transform cam = PlayerSync.LocalCamera;
            // ([Test] SuskiApproche=t : a t, chacun se pose a 2,5 m du pantin -- Talk ne demande de l'aide qu'a moins de 12 m)
            float ap = T("SuskiApproche", 0f);
            if (ap > 0f && !approched && t > ap && Pelvis != null && rag != null && rag.gameObject.activeInHierarchy)
            {
                approched = true;
                Autotest.PutPlayer(Pelvis.position + new Vector3(2.5f, 1f, 0f));
                Log.Info("autotest suski (" + Role + ") : pres du pantin ; " + Probe());
            }
            // Prise
            string[] pr = Config.Get("Test", "SuskiPrise", "").Split(',');
            float pt; bool mineTest = pr.Length >= 2 && (pr[1] == Role);
            if (pr.Length >= 2 && float.TryParse(pr[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out pt) && mineTest && cam != null && rag != null)
            {
                float hold = T("SuskiTenue", 12f);
                if (t > pt && t < pt + hold)
                {
                    if (!testHold) { testHold = true; Log.Info("autotest suski (" + Role + ") : prend le pantin ; " + Probe()); Vector3 near = cam.position + cam.forward * 1.2f; Autotest.PutPlayer(Pelvis.position - cam.forward * 1.5f + Vector3.up * 0.2f); testHoldAt = near; }
                    if (Config.GetInt("Test", "SuskiLacherHaut", 0) != 0 && t > pt + hold - 4f) testHoldAt = cam.position + Vector3.up * 2.5f + cam.forward * 0.8f;
                    else if (Config.GetInt("Test", "SuskiSecousse", 0) != 0 && t > pt + hold - 2f) testHoldAt = Pelvis.position - cam.right * 6f;
                    else testHoldAt = cam.position + cam.forward * 1.2f;
                }
                else if (testHold && t >= pt + hold) { testHold = false; Log.Info("autotest suski (" + Role + ") : lache le pantin ; " + Probe()); }
            }
            // ([Test] SuskiRoute=t : l'hote pose la voiture de SuskiVoiture sur la route la plus proche -- au garage, poussee, elle bute contre ses murs)
            float ro = T("SuskiRoute", 0f);
            if (Session.IsHost && ro > 0f && !routeDone && t > ro) { routeDone = true; string[] vv = Config.Get("Test", "SuskiVoiture", "x,SATSUMA").Split(','); Log.Info("autotest suski (hote) : " + VehicleSync.TestPlaceOnRoad(vv.Length >= 2 ? vv[1] : "SATSUMA")); }
            // Voiture
            string[] vr = Config.Get("Test", "SuskiVoiture", "").Split(',');
            float vt;
            if (vr.Length >= 2 && float.TryParse(vr[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out vt) && testStep >= 2 && testStep < 3 && t > vt && owner == Session.LocalId && rag != null)
            {
                testStep = 3;
                Rigidbody cb = VehicleSync.Body(vr[1]);
                if (cb != null)
                {
                    Vector3 seat = cb.transform.TransformPoint(SuskiPlace(vr));
                    MoveRag(seat - Pelvis.position, cb);
                    Log.Info("autotest suski (" + Role + ") : pantin pose sur la banquette de " + vr[1] + " ; " + Probe());
                }
            }
            string[] dr = Config.Get("Test", "SuskiConduite", "").Split(',');
            float d1, d2;
            if (dr.Length >= 3 && dr[2] == Role && float.TryParse(dr[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d1) && float.TryParse(dr[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d2))
            {
                string cn = vr.Length >= 2 ? vr[1] : "SATSUMA";
                if (testStep == 3 && t > d1 - 4f) { testStep = 31; Log.Info("autotest suski (" + Role + ") : a la voiture : " + (VehicleSync.TestEnter(cn, false) ?? "?")); }
                if (testStep == 31 && t > d1 - 2f) { testStep = 4; Log.Info("autotest suski (" + Role + ") : au volant : " + (VehicleSync.TestEnter(cn, true) ?? "?")); }
                // (moteur eteint dans la sauvegarde d'essai : la voiture est poussee a SuskiVitesse m/s dans son axe, comme PousseVitesse, puis
                // freinee d'un coup : vitesse divisee par 1,25 a chaque image -- arret en un tiers de seconde)
                Rigidbody vb = VehicleSync.Body(cn);
                if (testStep == 4 && t > d1 && t < d2 && vb != null)
                {
                    // (mise en vitesse progressive, 3 m/s2 : d'un coup, le pantin restait sur la route)
                    Vector3 f = vb.transform.forward; f.y = 0f;
                    // (vitesse imposee chaque image, rampe de 3 m/s2 depuis d1 : relative a la vitesse courante, les freins du fourgon l'annulaient)
                    float vit = Mathf.Min(T("SuskiVitesse", 10f), 3f * (t - d1));
                    vb.velocity = f.normalized * vit + Vector3.up * Mathf.Min(vb.velocity.y, 0f);
                }
                if (testStep == 4 && t >= d2) { testStep = 5; Log.Info("autotest suski (" + Role + ") : freine fort a " + (vb != null ? (vb.velocity.magnitude * 3.6f).ToString("F0") : "?") + " km/h ; " + Probe()); }
                if (testStep == 5 && t < d2 + 3f && vb != null && Config.GetInt("Test", "SuskiFreinage", 1) != 0) { Vector3 hv = vb.velocity; vb.velocity = new Vector3(hv.x / 1.25f, hv.y, hv.z / 1.25f); }
                if (testStep == 5 && t >= d2 + 3f) { testStep = 6; Log.Info("autotest suski (" + Role + ") : arrete ; " + Probe()); }
            }
            // ([Test] SuskiPassager=t,place,car : 'invite' s'assied a cette place de la voiture a t)
            string[] sp = Config.Get("Test", "SuskiPassager", "").Split(',');
            float st0;
            if (!Session.IsHost && sp.Length >= 3 && !passDone && float.TryParse(sp[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out st0) && t > st0)
            {
                passDone = true; int pl; int.TryParse(sp[1], out pl);
                Log.Info("autotest suski (invite) : passager : " + Seats.TestSit(sp[2], pl));
            }
            // Lit, sommeil, note
            float lt = T("SuskiLit", 0f);
            if (lt > 0f && testStep >= 2 && testStep < 7 && t > lt && owner == Session.LocalId && rag != null)
            {
                testStep = 7;
                // (essai : Talk ne demande pas toujours de l'aide dans cette mise en place -- l'hote envoie PLAYERHELP comme Talk 'Ask for help')
                if (Session.IsHost && stages != null && stages.ActiveStateName != "Ragdoll active") { stages.SendEvent("PLAYERHELP"); Log.Info("autotest suski (hote) : PLAYERHELP envoye a SuskiStages (etait " + stages.ActiveStateName + ")"); }
                GameObject bed = Game.FindAny("JOBS/Suski/SuskiStages/Sleep1");
                if (bed != null)
                {
                    MoveRag(bed.transform.position + Vector3.up * 0.3f - Pelvis.position, null);
                    Autotest.PutPlayer(bed.transform.position + Vector3.right * 2f + Vector3.up * 0.5f);
                }
                Log.Info("autotest suski (" + Role + ") : pantin pose sur le lit Sleep1 (" + (bed != null ? bed.transform.position.ToString("F1") : "introuvable") + ") ; " + Probe());
            }
            float ds = T("SuskiDort", 0f);
            if (Session.IsHost && ds > 0f && t > ds && t < ds + 5f)
            {
                FsmBool ps = FsmVariables.GlobalVariables.FindFsmBool("PlayerSleeps");
                if (ps != null && !ps.Value) { ps.Value = true; Log.Info("autotest suski (hote) : PlayerSleeps vrai ; " + Probe()); }
            }
            else if (Session.IsHost && ds > 0f && t >= ds + 5f && t < ds + 6f) { FsmBool ps = FsmVariables.GlobalVariables.FindFsmBool("PlayerSleeps"); if (ps != null && ps.Value) ps.Value = false; }
            float rn = T("SuskiLireNote", 0f);
            if (!Session.IsHost && rn > 0f && t > rn && t < rn + 4f && note != null && note.gameObject.activeInHierarchy)
            {
                if (testStep < 8) { testStep = 8; Autotest.PutPlayer(note.position + Vector3.right * 0.6f - Vector3.up * 0.6f); Log.Info("autotest suski (invite) : devant la note"); }
                LookAt(note.position);
                // (essai : si le rayon de la souris ne la prend pas -- MousePickEvent a 1 m --, l'etat 'Wait button' (survol) est force, comme le jeu)
                if (t > rn + 2f && testStep == 8 && noteUse != null && noteUse.ActiveStateName != "Wait button")
                {
                    testStep = 9;
                    Log.Info("autotest suski (invite) : survol de la note force (etait " + noteUse.ActiveStateName + ") : " + Game.SetState(noteUse, "Wait button"));
                }
            }
            foreach (string s in Config.Get("Test", "SuskiCaptures", "").Split(','))
            {
                float at;
                if (!float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out at) || t < at - 1f || t > at + 1.5f || rag == null) continue;
                if (!testHold) LookAt(rag.gameObject.activeInHierarchy && Pelvis != null ? Pelvis.position + Vector3.up * 0.2f : note != null ? note.position : rag.position);
                if (t < at || lastShot >= at) continue;
                lastShot = at;
                string dir = System.IO.Path.Combine(Log.DataDir, "dumps");
                System.IO.Directory.CreateDirectory(dir);
                string png = System.IO.Path.Combine(dir, "suski-" + Role + "-t" + ((int)at).ToString("000") + ".png");
                Application.CaptureScreenshot(png);
                Log.Info("autotest suski (" + Role + ") : capture " + png + " ; " + Probe());
            }
            if (t < 20f || t > T("SuskiFin", 100f) || Time.realtimeSinceStartup < testNext) return;
            testNext = Time.realtimeSinceStartup + (t > 40f && t < T("SuskiDetail", 0f) ? 0.5f : 2f);
            Log.Info("autotest suski (" + Role + ") : " + Probe());
        }
    }
}
