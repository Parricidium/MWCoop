using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Bus (NPC_CARS/.../BUS) : conduit chez l'hote, copie cinematique chez l'invite (Traffic).
    //  - Arrets et attente : StopBus (NavigationAI : ralentit a un arret si le joueur y attend), Route::Start (porte,
    //    attente du joueur) et le LOD du bus (interieur allume pres du joueur) mesurent la distance au joueur LOCAL
    //    (GetDistance vers PLAYER). Chez l'hote ces actions visent un repere, MWCoop-BusJoueur, pose a chaque image
    //    sur le joueur qui compte : le plus proche du bus qui n'y est pas monte (a moins de 60 m), sinon le plus
    //    proche (positions des invites : PlayerSync). Seul, l'hote garde exactement le comportement du jeu.
    //  - Ticket (Ticket::Button) et sonnettes (StopButton1/2::Activate) : le clic de l'invite joue chez lui (il paie
    //    de son argent) puis part a l'hote, qui rejoue l'etat ("Pay trip", "Activate") sur SON bus, ecritures
    //    d'argent du joueur coupees : le bus considere le trajet paye, s'arrete au prochain arret.
    //  - Chez l'invite la logique du bus est coupee (Route::Start/Angrytimer/DrivingIssue, NavigationAI) : la porte
    //    suit l'hote (Route::Door DoorOpen, ouverture et fermeture rejouees), les sonnettes sont allumees comme chez
    //    lui, l'interieur (LOD, dont la logique est coupee par Traffic) allume a moins de 150 m, le ticket remis a
    //    zero quand celui de l'hote l'est.
    //  - Monte dans la copie, le joueur de l'invite suit le bus d'une image a l'autre (la copie est deplacee par sa
    //    position : le CharacterController ne serait pas entraine), debout ou assis par terre ; assis sur une place
    //    du jeu (DriveTrigger, SetParent du joueur sous le bus) il est accroche par le jeu lui-meme.
    // Messages : controle de Traffic, genre 11 (invite -> hote : [U8 1 ticket | 2 sonnette 1 | 3 sonnette 2]) et
    // genre 12 (hote -> invites, a chaque changement et toutes les 5 s : [U8 1 porte ouverte | 2 sonnettes allumees |
    // 4 ticket au repos | 8 Markku assis | 16 Signe assise | 32 passagers connus]).
    //  - Passagers (LOD/Passengers::Activate tire au sort Markku, Signe, les deux ou personne, a chaque allumage de
    //    l'interieur) : chez l'invite ce tirage est coupe et les passagers de l'hote montres (retour de JD, 07/10 : il ne
    //    voyait que le chauffeur).
    public static class Bus
    {
        const int K_ACT = 11, K_STATE = 12;
        const int A_TICKET = 1, A_RING1 = 2, A_RING2 = 3;

        class ActHook : ModHook
        {
            public override string Module { get { return "bus"; } }
            public int What;
            public override void OnEnter()
            {
                try { if (Replay.Depth == 0) OnLocalAct(What); } catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        static Transform bus;
        static PlayMakerFSM ticket, door;
        static readonly PlayMakerFSM[] rings = new PlayMakerFSM[2];
        static readonly bool[] ringHooked = new bool[2];
        static GameObject lod, stopButtons, markku, signe;
        static PlayMakerFSM paxFsm;
        static BoxCollider inside;
        static bool built, ticketHooked, retargeted, mutedLogic, riding, platformChecked;
        static float buildAt = -1, nextTry, nextState, nextFull, ticketAt = -100;
        static int lastFlags = -1, acts, states, retargets;
        static readonly HashSet<PlayMakerFSM> retargetDone = new HashSet<PlayMakerFSM>();
        static GameObject proxy;
        static string proxyWho = "";
        static Vector3 prevPos; static Quaternion prevRot; static bool prevOk;
        static Transform player;
        static object platform; static FieldInfo platformOn; static bool platformWas;

        public static void OnLevelLoaded()
        {
            bus = null; ticket = door = null; rings[0] = rings[1] = null; ringHooked[0] = ringHooked[1] = false;
            lod = stopButtons = markku = signe = null; paxFsm = null; inside = null; player = null; proxy = null; proxyWho = ""; retargetDone.Clear();
            built = ticketHooked = retargeted = mutedLogic = riding = prevOk = platformChecked = false;
            platform = null; platformOn = null;
            lastFlags = -1; acts = states = retargets = 0; ticketAt = -100; step = 0;
            buildAt = PlayerSync.InGame ? Time.realtimeSinceStartup + 8f : -1;
        }

        static void Build()
        {
            built = true;
            GameObject npc = Game.FindAny("NPC_CARS");
            if (npc != null)
                foreach (Rigidbody rb in npc.GetComponentsInChildren<Rigidbody>(true)) if (rb.name == "BUS" && rb.GetComponent("CarDynamics") != null) { bus = rb.transform; break; }
            if (bus == null) { Log.Warn("bus : NPC_CARS/.../BUS introuvable"); return; }
            Transform t = bus.Find("Ticket");
            ticket = t != null ? Game.FsmOn(t.gameObject, "Button") : null;
            Transform r = bus.Find("Route");
            door = r != null ? Game.FsmOn(r.gameObject, "Door") : null;
            Transform l = bus.Find("LOD");
            lod = l != null ? l.gameObject : null;
            if (l != null)
            {
                Transform sb = l.Find("StopButtons");
                stopButtons = sb != null ? sb.gameObject : null;
                for (int i = 0; i < 2 && sb != null; i++)
                {
                    Transform b = sb.Find("StopButton" + (i + 1));
                    rings[i] = b != null ? Game.FsmOn(b.gameObject, "Activate") : null;
                }
                Transform pt = l.Find("PlayerTrigger");
                inside = pt != null ? pt.GetComponent<BoxCollider>() : null;
                Transform pax = l.Find("Passengers");
                if (pax != null)
                {
                    Transform m = pax.Find("Markku"), s = pax.Find("Signe");
                    markku = m != null ? m.gameObject : null; signe = s != null ? s.gameObject : null;
                    paxFsm = Game.FsmOn(pax.gameObject, "Activate");
                    if (!Own(paxFsm)) paxFsm = null;
                }
            }
            // Ticket et sonnettes : a ce module (personne d'autre ne suit NPC_CARS).
            ticketHooked = !Own(ticket);
            for (int i = 0; i < 2; i++) ringHooked[i] = !Own(rings[i]);
            Log.Info("bus : " + Recon.Path(bus) + ", ticket " + (ticket != null) + ", porte " + (door != null) + ", sonnettes " + (rings[0] != null) + "/" + (rings[1] != null)
                     + ", interieur " + (inside != null) + ", passagers " + (markku != null) + "/" + (signe != null));
        }

        static bool Own(PlayMakerFSM f)
        {
            if (f == null) return false;
            if (Replay.Claim(f, "bus")) return true;
            Log.Warn("bus : " + Recon.Path(f.transform) + "::" + f.FsmName + " deja pris par " + Replay.Owner(f));
            return false;
        }

        static bool Ready(PlayMakerFSM f)
        {
            if (f == null || !f.gameObject.activeInHierarchy) return false;
            foreach (FsmState s in f.Fsm.States) if (!s.IsInitialized) return false;
            return true;
        }

        static void Hook(PlayMakerFSM f, string state, int what)
        {
            FsmState st = f.Fsm.GetState(state);
            if (st == null) { Log.Warn("bus : etat '" + state + "' absent de " + f.FsmName); return; }
            var list = new List<FsmStateAction>(st.Actions);
            list.Insert(0, new ActHook { What = what });
            st.Actions = list.ToArray();
            Log.Info("bus : " + f.gameObject.name + "::" + f.FsmName + " \"" + state + "\" suivi");
        }

        // Apres le suivi du trafic (la copie de l'invite est a sa place de cette image).
        public static void Update()
        {
            float now = Time.realtimeSinceStartup;
            if (!built) { if (buildAt > 0 && now >= buildAt) Build(); return; }
            if (bus == null || !Session.Active) return;
            if (now >= nextTry)
            {
                nextTry = now + 1f;
                if (!ticketHooked && Ready(ticket)) { ticketHooked = true; Hook(ticket, "Pay trip", A_TICKET); }
                for (int i = 0; i < 2; i++) if (!ringHooked[i] && Ready(rings[i])) { ringHooked[i] = true; Hook(rings[i], "Activate", A_RING1 + i); }
                if (Session.IsHost && !retargeted) Retarget();
                if (!Session.IsHost && !mutedLogic && Traffic.Follows(bus)) MuteLogic();
            }
            if (Session.IsHost) { Proxy(); SendState(now); return; }
            if (mutedLogic) Interior();
            Ride();
        }

        // ---------------------------------------------------------------- hote
        // Actions GetDistance des automates du bus (racine : LOD ; NavigationAI ; Route) qui visent PLAYER : visent le
        // repere. Une fois les actions chargees (bus actif).
        static void Retarget()
        {
            var fsms = new List<PlayMakerFSM>(bus.GetComponents<PlayMakerFSM>());
            foreach (string c in new[] { "NavigationAI", "Route" })
            {
                Transform t = bus.Find(c);
                if (t != null) fsms.AddRange(t.GetComponents<PlayMakerFSM>());
            }
            if (proxy == null) proxy = new GameObject("MWCoop-BusJoueur");
            var sb = new System.Text.StringBuilder();
            int left = 0, before = retargets;
            foreach (PlayMakerFSM f in fsms)
            {
                if (retargetDone.Contains(f)) continue;
                if (!Ready(f)) { left++; continue; }   // actions pas encore chargees : au prochain passage
                retargetDone.Add(f);
                foreach (FsmState st in f.Fsm.States)
                    foreach (FsmStateAction a in st.Actions)
                    {
                        if (a == null || a.GetType().Name != "GetDistance") continue;
                        foreach (FieldInfo fi in a.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                        {
                            object v = fi.GetValue(a);
                            var g = v as FsmGameObject;
                            if (g != null && IsPlayer(g.Value)) { fi.SetValue(a, new FsmGameObject { Value = proxy }); }
                            else
                            {
                                var od = v as FsmOwnerDefault;
                                if (od == null || od.OwnerOption == OwnerDefaultOption.UseOwner || od.GameObject == null || !IsPlayer(od.GameObject.Value)) continue;
                                od.GameObject = new FsmGameObject { Value = proxy };
                            }
                            retargets++;
                            if (sb.Length < 600) sb.Append(' ').Append(f.FsmName).Append('/').Append(st.Name).Append('.').Append(fi.Name);
                        }
                    }
            }
            retargeted = left == 0;
            if (retargets != before) Log.Info("bus : " + (retargets - before) + " mesures de distance au joueur visent le joueur qui compte" + (left > 0 ? " (" + left + " automates pas encore charges)" : "") + " :" + sb);
        }

        static bool IsPlayer(GameObject g) { return g != null && g.name == "PLAYER" && g.transform.parent == null; }

        // Le joueur qui compte : le plus proche du bus hors du bus (a moins de 60 m), sinon le plus proche.
        static void Proxy()
        {
            if (proxy == null) return;
            if (player == null) { GameObject p = GameObject.Find("PLAYER"); if (p == null) return; player = p.transform; }
            Vector3 bp = bus.position, mine = player.position;
            // Pieds -> origine du joueur (comme PLAYER, que mesuraient les actions).
            Vector3 lift = Session.Me != null && Session.Me.State.Feet != Vector3.zero ? mine - Session.Me.State.Feet : Vector3.up * 0.9f;
            Vector3 best = mine, outBest = Vector3.zero;
            string who = "hote", outWho = null;
            float bestD = (mine - bp).sqrMagnitude, outD = 60f * 60f;
            if (!Inside(mine) && bestD < outD) { outD = bestD; outBest = mine; outWho = "hote"; }
            foreach (Avatar a in PlayerSync.Avatars)
            {
                if (a.Player == null || a.Player.Level != 1) continue;
                Vector3 p = a.Player.State.Feet + lift;
                float d = (p - bp).sqrMagnitude;
                if (d < bestD) { bestD = d; best = p; who = "#" + a.Player.Id; }
                if (d < outD && !Inside(p)) { outD = d; outBest = p; outWho = "#" + a.Player.Id; }
            }
            if (outWho != null) { best = outBest; who = outWho; }
            proxy.transform.position = best;
            if (who != proxyWho) { proxyWho = who; if (++states <= 40) Log.Info("bus : le bus compte avec " + who); }
        }

        static void SendState(float now)
        {
            if (Session.RemoteCount == 0 || now < nextState) return;
            nextState = now + 0.2f;
            int flags = 0;
            FsmBool open = door != null ? door.FsmVariables.FindFsmBool("DoorOpen") : null;
            if (open != null && open.Value) flags |= 1;
            if (stopButtons != null && stopButtons.activeSelf) flags |= 2;
            if (ticket != null && (ticket.ActiveStateName == "Wait" || ticket.ActiveStateName == "Wait for click")) flags |= 4;
            if (markku != null && signe != null) flags |= 32 | (markku.activeSelf ? 8 : 0) | (signe.activeSelf ? 16 : 0);
            if (flags == lastFlags && now < nextFull) return;
            lastFlags = flags; nextFull = now + 5f;
            Session.Broadcast(new NetWriter(Msg.Traffic).U16(0xFFFF).U8(K_STATE).U8(flags), true);
        }

        // Ticket paye, sonnette : rejoue sur le bus de l'hote, argent du joueur intouche.
        static void Play(PlayMakerFSM f, string state, int who)
        {
            if (f == null) { Log.Warn("bus : action de #" + who + " sans automate ici"); return; }
            if (!f.gameObject.activeInHierarchy) { Log.Info("bus : action de #" + who + " ignoree (" + f.gameObject.name + " eteint ici)"); return; }
            var muted = new List<FsmStateAction>();
            FsmState st = f.Fsm.GetState(state);
            if (st != null)
                foreach (FsmStateAction a in st.Actions)
                    if (a != null && a.Enabled && !(a is ModHook) && WritesPlayer(f, a)) { a.Enabled = false; muted.Add(a); }
            Replay.Depth++;
            try { Game.SetState(f, state); }
            finally
            {
                Replay.Depth--;
                foreach (FsmStateAction a in muted) a.Enabled = true;
            }
            Log.Info("bus : " + f.gameObject.name + " \"" + state + "\" de #" + who + " rejoue (" + muted.Count + " ecritures du joueur coupees)");
        }

        static bool WritesPlayer(PlayMakerFSM f, FsmStateAction a)
        {
            if (a.GetType().Name.StartsWith("Get")) return false;
            foreach (FieldInfo fi in a.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var nv = fi.GetValue(a) as NamedVariable;
                if (nv != null && nv.UseVariable && (nv.Name.StartsWith("Player") || nv.Name.StartsWith("GUI")) && !Game.LocalVar(f, nv.Name)) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- invite
        static void OnLocalAct(int what)
        {
            if (!Session.Active || Session.IsHost) return;
            if (what == A_TICKET) ticketAt = Time.realtimeSinceStartup;
            acts++;
            Log.Info("bus : " + (what == A_TICKET ? "ticket paye" : "sonnette " + (what - 1)) + " ici, envoye a l'hote");
            Session.SendToHost(new NetWriter(Msg.Traffic).U16(0xFFFF).U8(K_ACT).U8(what), true);
        }

        static void MuteLogic()
        {
            mutedLogic = true;
            int n = 0;
            Transform r = bus.Find("Route"), nav = bus.Find("NavigationAI");
            if (r != null)
                foreach (PlayMakerFSM f in r.GetComponents<PlayMakerFSM>())
                    if (f.FsmName != "Door" && f.enabled) { f.enabled = false; n++; }
            if (nav != null)
                foreach (PlayMakerFSM f in nav.GetComponents<PlayMakerFSM>()) if (f.enabled) { f.enabled = false; n++; }
            if (paxFsm != null && paxFsm.enabled) { paxFsm.enabled = false; n++; }   // (passagers : ceux de l'hote)
            Log.Info("bus : logique du bus coupee ici (" + n + " automates), porte et sonnettes de l'hote");
        }

        // Interieur allume pres du joueur (la logique LOD du bus est coupee par Traffic).
        static void Interior()
        {
            if (lod == null) return;
            if (player == null) { GameObject p = GameObject.Find("PLAYER"); if (p == null) return; player = p.transform; }
            bool near = bus.gameObject.activeInHierarchy && (player.position - bus.position).sqrMagnitude < 150f * 150f;
            if (lod.activeSelf != near) lod.SetActive(near);
        }

        // Monte dans la copie : suit son deplacement de l'image.
        static void Ride()
        {
            if (player == null) { GameObject p = GameObject.Find("PLAYER"); if (p == null) return; player = p.transform; }
            bool on = bus.gameObject.activeInHierarchy && player.parent == null && Inside(player.position);
            if (on && prevOk)
            {
                Quaternion dq = bus.rotation * Quaternion.Inverse(prevRot);
                Vector3 np = bus.position + dq * (player.position - prevPos);
                if ((np - player.position).sqrMagnitude > 1e-8f)
                {
                    player.position = np;
                    float yaw = dq.eulerAngles.y;
                    if (Mathf.Abs(Mathf.DeltaAngle(0f, yaw)) > 0.001f) player.rotation = Quaternion.Euler(0f, yaw, 0f) * player.rotation;
                }
            }
            if (on != riding)
            {
                riding = on;
                Platform(on);
                Log.Info("bus : joueur " + (on ? "monte dans le bus (suit la copie)" : "descendu du bus"));
            }
            prevPos = bus.position; prevRot = bus.rotation; prevOk = bus.gameObject.activeInHierarchy;
        }

        // Plateforme mobile du CharacterMotor (si le jeu l'a) : coupee dans le bus, sinon le deplacement compterait
        // deux fois.
        static void Platform(bool riding)
        {
            if (!platformChecked)
            {
                platformChecked = true;
                Component m = player.GetComponent("CharacterMotor");
                FieldInfo fp = m != null ? m.GetType().GetField("movingPlatform") : null;
                platform = fp != null ? fp.GetValue(m) : null;
                platformOn = platform != null ? platform.GetType().GetField("enabled") : null;
                Log.Info("bus : plateforme mobile du joueur " + (platformOn != null ? (bool)platformOn.GetValue(platform) ? "active (coupee dans le bus)" : "inactive" : "absente"));
            }
            if (platformOn == null) return;
            if (riding) { platformWas = (bool)platformOn.GetValue(platform); platformOn.SetValue(platform, false); }
            else platformOn.SetValue(platform, platformWas);
        }

        static bool Inside(Vector3 p)
        {
            if (inside == null) return false;
            Vector3 l = inside.transform.InverseTransformPoint(p) - inside.center, h = inside.size * 0.5f;
            return Mathf.Abs(l.x) <= h.x + 0.3f && Mathf.Abs(l.y) <= h.y + 0.6f && Mathf.Abs(l.z) <= h.z + 0.3f;
        }

        // ---------------------------------------------------------------- messages (controle de Traffic)
        public static void OnMessage(int kind, Peer from, NetReader r)
        {
            if (bus == null) return;
            if (kind == K_ACT)
            {
                if (!Session.IsHost) return;
                int what = r.U8();
                acts++;
                if (what == A_TICKET) Play(ticket, "Pay trip", from.Id);
                else if (what == A_RING1 || what == A_RING2) Play(rings[what - A_RING1], "Activate", from.Id);
                return;
            }
            if (kind != K_STATE || Session.IsHost) return;
            int flags = r.U8();
            states++;
            if (stopButtons != null && stopButtons.activeSelf != ((flags & 2) != 0)) stopButtons.SetActive((flags & 2) != 0);
            if ((flags & 32) != 0 && markku != null && signe != null)
            {
                if (paxFsm != null && paxFsm.enabled) paxFsm.enabled = false;   // (avant meme le suivi de la copie)
                bool m = (flags & 8) != 0, s = (flags & 16) != 0;
                if (markku.activeSelf != m || signe.activeSelf != s)
                {
                    markku.SetActive(m); signe.SetActive(s);
                    Log.Info("bus : passagers de l'hote : " + (m && s ? "Markku et Signe" : m ? "Markku" : s ? "Signe" : "personne"));
                }
            }
            // Porte : au repos ici (State 2) et differente de celle de l'hote -> ouverture ou fermeture du jeu.
            FsmBool open = door != null ? door.FsmVariables.FindFsmBool("DoorOpen") : null;
            bool hostOpen = (flags & 1) != 0;
            if (open != null && open.Value != hostOpen && door.enabled && door.gameObject.activeInHierarchy && door.ActiveStateName == "State 2")
            {
                Replay.Depth++;
                try { Game.SetState(door, hostOpen ? "Driver open" : "Driver close"); }
                finally { Replay.Depth--; }
            }
            // Ticket de l'hote remis a zero (nouveau trajet) : le notre aussi, s'il est paye depuis plus de 10 s.
            if ((flags & 4) != 0 && ticket != null && ticket.ActiveStateName == "State 1" && Time.realtimeSinceStartup - ticketAt > 10f && ticket.gameObject.activeInHierarchy)
            {
                Replay.Depth++;
                try { Game.SetState(ticket, "Wait"); }
                finally { Replay.Depth--; }
                Log.Info("bus : ticket remis a zero comme chez l'hote");
            }
        }

        // ---------------------------------------------------------------- essais
        // 'bus' : les deux cotes notent le bus toutes les 2 s. Invite : a [Test] BusArrivee (35 s) teleporte dans le
        // bus ([Test] BusDedans=1, defaut) ou a 4 m de sa porte (0) ; a BusTicket (40 s) paie le ticket (Button
        // "State 2" -> "Pay trip") ; a BusSonnette (45 s) sonne (StopButton1 "Activate"). Attendu chez l'hote :
        // "Pay trip de #1 rejoue", "Activate de #1 rejoue", "le bus compte avec #1".
        static int step;
        static float nextTest;

        public static void Test(string mode, float t)
        {
            if (mode != "bus") return;
            if (!Session.IsHost && bus != null)
            {
                if (step == 0 && t > Config.GetInt("Test", "BusArrivee", 35)) { step = 1; Log.Info("autotest : bus " + TestArrive(Config.GetInt("Test", "BusDedans", 1) != 0)); }
                if (step == 1 && t > Config.GetInt("Test", "BusTicket", 40))
                {
                    step = 2;
                    if (ticket == null) Log.Info("autotest : bus : pas de ticket");
                    else { string b = ticket.ActiveStateName; Game.SetState(ticket, "State 2"); Log.Info("autotest : bus ticket " + b + " => " + ticket.ActiveStateName); }
                }
                if (step == 2 && t > Config.GetInt("Test", "BusSonnette", 45))
                {
                    step = 3;
                    if (rings[0] == null || !Ready(rings[0])) Log.Info("autotest : bus : sonnette eteinte ici (" + (stopButtons != null && stopButtons.activeInHierarchy) + ")");
                    else { Game.SetState(rings[0], "Activate"); Log.Info("autotest : bus sonnette 1 => " + rings[0].ActiveStateName); }
                }
            }
            float now = Time.realtimeSinceStartup;
            if (t < 20f || now < nextTest) return;
            nextTest = now + 2f;
            Log.Info("autotest : bus (" + (Session.IsHost ? "hote" : "invite") + ") " + State());
        }

        static string State()
        {
            if (bus == null) return built ? "absent" : "pas encore releve";
            FsmBool open = door != null ? door.FsmVariables.FindFsmBool("DoorOpen") : null;
            var sb = new System.Text.StringBuilder();
            sb.Append(bus.position.ToString("F1")).Append(bus.gameObject.activeInHierarchy ? " actif" : " inactif")
              .Append(", porte ").Append(open != null ? (open.Value ? "ouverte" : "fermee") : "?").Append(" (").Append(door != null ? door.ActiveStateName : "?").Append(')')
              .Append(", ticket ").Append(ticket != null ? ticket.ActiveStateName : "?")
              .Append(", sonnettes ").Append(stopButtons != null && stopButtons.activeInHierarchy)
              .Append(", interieur ").Append(lod != null && lod.activeInHierarchy);
            if (Session.IsHost) sb.Append(", compte avec ").Append(proxyWho).Append(", ").Append(retargets).Append(" mesures redirigees");
            else sb.Append(", logique ").Append(mutedLogic ? "coupee" : "locale").Append(", dans le bus ").Append(riding);
            if (player != null) sb.Append(", joueur ").Append(player.position.ToString("F1"));
            sb.Append(", actions ").Append(acts).Append(", etats ").Append(states);
            return sb.ToString();
        }

        static string TestArrive(bool inBus)
        {
            GameObject pl = GameObject.Find("PLAYER");
            if (pl == null) return "pas de joueur";
            Vector3 to;
            if (inBus && inside != null) to = inside.transform.TransformPoint(inside.center) + Vector3.up * 0.2f;
            else
            {
                Transform d = lod != null ? lod.transform.Find("Door") : null;
                to = (d != null ? d.position : bus.position) + bus.right * 4f + Vector3.up * 0.5f;
            }
            var cc = pl.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            pl.transform.position = to;
            if (cc != null) cc.enabled = true;
            return "invite teleporte " + (inBus ? "dans le bus" : "a sa porte") + " en " + to.ToString("F1");
        }
    }
}
