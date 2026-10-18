using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Position, regard et posture de chaque joueur ; un avatar (PNJ du jeu) par joueur distant.
    public static class PlayerSync
    {
        // Posture et actions du joueur, montrees par son avatar (Avatar).
        public const int F_Crouch = 1, F_Seated = 2, F_Smoke = 4, F_Drink = 8, F_Carry = 16, F_Hello = 32, F_Sleep = 64,
            F_SleepFast = 128,                      // le lit compte les heures : quand tous l'ont, l'hote accelere
            F_Inhale = 256, F_Exhale = 512,         // cigarette : tire (main a la bouche), souffle (fumee)
            F_Jacket = 1024, F_Coverall = 2048, F_Helmet = 4096,   // vetements portes (Wear) : caches chez les autres, montres par l'avatar
            F_Dead = 8192;                          // mort, en train de choisir ou revenir (Respawn) : avatar en pantin (ragdoll)
        const float SendRate = 1f / 20f;
        static float nextSend;
        static Transform player, cam, smoking, drinking, hello;
        public static Transform LocalCamera { get { return cam; } }
        public static CharacterController LocalController { get { return controller; } }
        // Pieds du joueur local (dernier etat envoye).
        public static Vector3 LocalFeet { get { return Session.Me != null ? Session.Me.State.Feet : Vector3.zero; } }
        static CharacterController controller;
        static PlayMakerFSM crouchFsm, smokeFsm;
        static readonly Dictionary<int, Avatar> avatars = new Dictionary<int, Avatar>();

        static PlayerSync()
        {
            Session.PlayerLeft += pi => { RemoveAvatar(pi.Id); Seats.PlayerLeft(pi.Id); Gestures.PlayerLeft(pi.Id); HandTools.PlayerLeft(pi.Id); Stock.PlayerLeft(pi.Id); CarDoors.PlayerLeft(pi.Id); Voice.PlayerLeft(pi.Id); resyncBy.Remove(pi.Id); };
        }

        public static bool InGame { get { return Game.LevelName == "GAME"; } }

        public static void OnLevelLoaded()
        {
            player = cam = null; crouchFsm = null; smokeFsm = null;
            controller = null;
            resyncAt = -1f; resyncSince = -1f; resyncIn.Clear(); testStep = 0;
            foreach (Avatar a in avatars.Values) a.Destroy();
            avatars.Clear();
            Avatar.ResetTemplate();
        }

        static bool FindPlayer()
        {
            if (player != null) return true;
            GameObject go = Game.PlayerGo;
            if (go == null) return false;
            player = go.transform;
            controller = go.GetComponent<CharacterController>();
            // La vraie camera (composant Camera) est FPSCamera/FPSCamera ; l'FPSCamera exterieure porte le
            // MouseLook et n'a pas forcement la meme orientation.
            Transform c = player.Find("Pivot/AnimPivot/Camera/FPSCamera/FPSCamera") ?? player.Find("Pivot/AnimPivot/Camera/FPSCamera");
            cam = c != null ? c : player;
            // Mains a la premiere personne : actives seulement pendant l'action.
            Transform fps = player.Find("Pivot/AnimPivot/Camera/FPSCamera/FPSCamera");
            // Drink reste actif (conteneur) : ce sont ses mains HandJuice, HandCoffeeHome... qui s'allument.
            if (fps != null) { smoking = fps.Find("Smoking"); drinking = fps.Find("Drink/Hand"); hello = fps.Find("Hello"); }
            return true;
        }

        public static void Update()
        {
            if (!Session.Active) return;
            Session.Me.Level = InGame ? 1 : 0;
            float now = Time.realtimeSinceStartup;
            if (now >= nextSend)
            {
                nextSend = now + SendRate;
                SendLocal();
            }
            if (!InGame) return;
            foreach (PlayerInfo pi in Session.Players.Values)
            {
                if (pi.Local) continue;
                Avatar a;
                bool want = pi.Level == 1 && pi.StateTime > 0;
                if (!avatars.TryGetValue(pi.Id, out a))
                {
                    if (!want) continue;
                    a = Avatar.Create(pi);
                    if (a == null) continue;
                    avatars[pi.Id] = a;
                }
                if (!want) { RemoveAvatar(pi.Id); continue; }
                if (a.Body != Looks.Parse(pi.Skin).Body) { RemoveAvatar(pi.Id); continue; }   // autre corps : refait a l'image suivante
                a.Apply(pi);
            }
        }

        // Commande actionnee par ce joueur (Jobs) : son avatar y tend la main.
        public static void ReachFor(int id, Vector3 at)
        {
            Avatar a;
            if (avatars.TryGetValue(id, out a) && a != null && a.Root != null) a.ReachFor(at);
        }
        // Repere de la tete (camera) du joueur 'id' : tel que recu (net), ou sur son avatar affiche (pieds lisses, cap
        // de l'avatar, meme inclinaison). Props : un objet tenu par lui est pose dans ce repere, colle a l'avatar.
        public static bool HeadFrame(int id, bool displayed, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            PlayerInfo pi;
            if (!Session.Players.TryGetValue(id, out pi) || pi.Local) return false;
            PlayerState st = pi.State;
            if (!displayed) { pos = st.Head; rot = Quaternion.Euler(st.Pitch, st.Yaw, 0f); return true; }
            Avatar a;
            if (!avatars.TryGetValue(id, out a) || a == null || a.Root == null) return false;
            pos = st.Head + (a.Root.transform.position - st.Feet);
            rot = Quaternion.Euler(st.Pitch, a.Root.transform.eulerAngles.y, 0f);
            return true;
        }

        public static Transform HeadOf(int id)
        {
            Avatar a;
            return avatars.TryGetValue(id, out a) && a != null && a.Root != null ? a.HeadBone : null;
        }

        public static Transform RootOf(int id)
        {
            Avatar a;
            return avatars.TryGetValue(id, out a) && a != null && a.Root != null ? a.Root.transform : null;
        }

        public static Transform HandOf(int id)
        {
            Avatar a;
            return avatars.TryGetValue(id, out a) && a != null && a.Root != null ? a.HandRight : null;
        }

        public static void ReachFor(int id, Transform t)
        {
            Avatar a;
            if (avatars.TryGetValue(id, out a) && a != null && a.Root != null) a.ReachFor(t);
        }

        // Commande actionnee par un autre dont on ne sait pas qui (message relaye sans auteur : molettes) : l'avatar le plus
        // proche, a moins de 2,5 m, y tend la main -- au plus une fois par demi-seconde pour la meme commande (la molette
        // envoie sa valeur en continu).
        static readonly Dictionary<Transform, float> reachNear = new Dictionary<Transform, float>();
        public static void ReachNearest(Transform t)
        {
            if (t == null) return;
            float now = Time.realtimeSinceStartup, at;
            if (reachNear.TryGetValue(t, out at) && now - at < 0.5f) return;
            reachNear[t] = now;
            Avatar best = null; float bd = 2.5f * 2.5f;
            foreach (Avatar a in avatars.Values)
            {
                if (a == null || a.Root == null) continue;
                float d = (a.Root.transform.position + Vector3.up * 1.2f - t.position).sqrMagnitude;
                if (d < bd) { bd = d; best = a; }
            }
            if (best != null) best.ReachFor(t);
        }

        // Apres les animations : poses calculees (conduite, buste et tete qui suivent le regard, bras).
        public static void LateUpdate()
        {
            if (!InGame) return;
            foreach (Avatar a in avatars.Values) a.LatePose();
            ThirdPerson.LatePose();
        }

        static bool AnyChildActive(Transform t)
        {
            if (t == null || !t.gameObject.activeInHierarchy) return false;
            foreach (Transform c in t) if (c.gameObject.activeSelf) return true;
            return false;
        }

        static float sleepLogAt;
        static void SendLocal()
        {
            PlayerState st = BuildLocal();
            Session.Me.State = st;
            var w = new NetWriter(Msg.PlayerState).U8(Session.LocalId).U8(Session.Me.Level)
                .Vec(st.Feet).Vec(st.Head).F32(st.Yaw).F32(st.Pitch).F32(st.Height).F32(st.Speed).U16(st.Flags).U8(st.Drink);
            Session.SendAll(w, false);
        }

        // Etat du joueur local (envoye 20 fois/s ; a chaque image pour son avatar a la troisieme personne).
        public static PlayerState BuildLocal()
        {
            var st = new PlayerState();
            if (InGame && FindPlayer())
            {
                float h = controller != null ? controller.height : 1.8f;
                Vector3 center = controller != null ? player.TransformPoint(controller.center) : player.position;
                st.Feet = center - Vector3.up * h * 0.5f;
                st.Head = ThirdPerson.EyeOf(cam);   // (troisieme personne : la camera n'est reculee que pendant le rendu)
                // Regard : direction de la camera (le corps du joueur ne tourne pas toujours avec elle).
                Vector3 fw = cam.forward;
                Vector3 flat = new Vector3(fw.x, 0f, fw.z);
                st.Yaw = flat.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(flat).eulerAngles.y : player.eulerAngles.y;
                st.Pitch = -Mathf.Asin(Mathf.Clamp(fw.y, -1f, 1f)) * Mathf.Rad2Deg;
                if (DroneCam.Active) { st.Yaw = DroneCam.BodyYaw; st.Pitch = DroneCam.BodyPitch; }   // (yeux au ciel pendant le drone : regard d'avant)
                string forced = Config.Get("Test", "TestRegard", "");
                if (forced.Length > 0) st.Pitch = float.Parse(forced, System.Globalization.CultureInfo.InvariantCulture);
                // Hauteur de la camera voulue par l'automate Crouch du jeu (1,4 debout, 0,85 accroupi, 0,3 au
                // ras du sol) : l'avatar en tire sa posture (accroupi, puis a genoux penche).
                if (crouchFsm == null) crouchFsm = Game.FsmOn(player.gameObject, "Crouch");
                FsmFloat cpos = crouchFsm != null ? crouchFsm.FsmVariables.FindFsmFloat("Position") : null;
                st.Height = cpos != null ? cpos.Value : 1.4f;
                st.Speed = controller != null ? new Vector3(controller.velocity.x, 0, controller.velocity.z).magnitude : 0f;
                // Accroupi : hauteur nettement sous la hauteur debout (la plus grande vue).
                // Accroupi : la camera descend nettement sous sa hauteur debout (la plus grande vue a pied).
                // Assis : au volant, passager (Seats), ou sur un siege du jeu (chaise, canape, banc : PlayerSeated, que
                // l'automate Crouch suit en "Seated" -- camera abaissee).
                bool seatedNow = Game.GlobalBool("PlayerSeated") || crouchFsm != null && crouchFsm.ActiveStateName == "Seated" || VehicleSync.LocalDriving >= 0 || Seats.Seated;
                if (!seatedNow && st.Height < 1.2f) st.Flags |= F_Crouch;
                if (seatedNow) st.Flags |= F_Seated;
                if (Game.GlobalBool("PlayerSleeps")) st.Flags |= F_Sleep;
                if (Game.GlobalBool("PlayerSleeps") && World.BedCounting()) st.Flags |= F_SleepFast;
                // Au lit, le jeu couche le joueur avec le pivot du lit (AnimPivotSleep, animation sleep_bed_in) : son « haut »
                // pointe vers la tete du lit. Le cap envoye est alors celui des pieds : l'avatar se couche dans l'axe du lit
                // (le regard, lui, ne le donne pas : corps a travers le mur).
                if ((st.Flags & F_Sleep) != 0)
                {
                    Vector3 up = player.up;
                    Vector3 feet = new Vector3(-up.x, 0f, -up.z);
                    if (feet.sqrMagnitude > 0.25f) st.Yaw = Quaternion.LookRotation(feet).eulerAngles.y;
                    if (Time.realtimeSinceStartup >= sleepLogAt) { sleepLogAt = Time.realtimeSinceStartup + 30f; Log.Info("sommeil : haut du joueur " + up.ToString("F2") + ", regard " + cam.forward.ToString("F2") + ", cap des pieds " + st.Yaw.ToString("F0") + ", camera " + cam.position.ToString("F2") + ", joueur " + player.position.ToString("F2") + (player.parent != null ? ", pivot " + player.parent.name + " " + player.parent.position.ToString("F2") : "")); }
                }
                if (smoking != null && smoking.gameObject.activeInHierarchy)
                {
                    st.Flags |= F_Smoke;
                    // Tirer sur la cigarette (touche tenue) : la main a la bouche ; relacher : la fumee.
                    if (smokeFsm == null) smokeFsm = Game.FsmOn(smoking.gameObject, "Start");
                    string sm = smokeFsm != null ? smokeFsm.ActiveStateName : "";
                    if (sm == "Anim 2" || sm == "Explosion?" || sm == "Inhale") st.Flags |= F_Inhale;
                    else if (sm == "Anim 3" || sm == "Outhale" || sm == "Anim 4" || sm == "Outhale 2") st.Flags |= F_Exhale;
                }
                if (AnyChildActive(drinking) || Game.GlobalBool("PlayerDrinkOn") || Time.realtimeSinceStartup < Consume.EatUntil) st.Flags |= F_Drink;
                st.Drink = (st.Flags & F_Drink) != 0 ? Drinks.LocalIndex() : 0;
                if (hello != null && hello.gameObject.activeInHierarchy) st.Flags |= F_Hello;
                if (Props.Holding) st.Flags |= F_Carry;
                if (Respawn.Choosing || Autotest.TestDead) st.Flags |= F_Dead;
                st.Flags |= Wear.LocalFlags;
                st.Flags |= Config.GetInt("Test", "TestFlags", 0) | Autotest.PoseFlags;   // essais : postures forcees
                if ((st.Flags & F_Drink) != 0 && st.Drink == 0) st.Drink = Config.GetInt("Test", "TestBoisson", 0);   // (essais : quelle boisson)
                if (Autotest.DrinkCycle > 0) { st.Flags |= F_Drink; st.Drink = Autotest.DrinkCycle; }
            }
            return st;
        }

        public static void OnState(Peer from, NetReader r, byte[] raw, int off, int len)
        {
            int id = r.U8();
            if (Session.IsHost) id = from.Id;          // un invite ne parle que pour lui-meme
            PlayerInfo pi;
            if (!Session.Players.TryGetValue(id, out pi) || pi.Local) return;
            int level = r.U8();
            var st = new PlayerState { Feet = r.Vec(), Head = r.Vec(), Yaw = r.F32(), Pitch = r.F32(), Height = r.F32(), Speed = r.F32(), Flags = r.U16(), Drink = r.U8() };
            bool levelChanged = pi.Level != level;
            pi.Level = level;
            pi.State = st;
            pi.StateTime = Time.realtimeSinceStartup;
            if (Session.IsHost)
            {
                // Relais aux autres invites, avec le bon numero de joueur.
                var w = new NetWriter(Msg.PlayerState).U8(id).U8(level)
                    .Vec(st.Feet).Vec(st.Head).F32(st.Yaw).F32(st.Pitch).F32(st.Height).F32(st.Speed).U16(st.Flags).U8(st.Drink);
                Session.Broadcast(w, false, id);
                if (levelChanged) Session.SendRoster();
                // Arrive en jeu : etat complet (portes, liquides, objets consommes pendant son chargement...).
                if (levelChanged && level == 1) SnapshotAll(from, "arrivee de #" + from.Id);
            }
        }

        // ---------------------------------------------------------------- etat complet (arrivee, resynchronisation)
        // Hote : tout ce qu'un invite recoit en arrivant en jeu -- chaque module l'envoie quelques secondes plus tard, le temps
        // que ses objets soient releves chez l'arrivant. Aussi sur demande (@resync, bouton « Resynchroniser le monde » de
        // l'onglet SYNCHRO du menu F10, 16/10) : un invite dont l'etat a derive sans bruit se recale sur celui de l'hote, qui
        // seul fait foi (sa sauvegarde). Renvoie le nombre de modules sollicites.
        // Pas ici : Fires, Cooking (25 s) et Frost (28 s) reperent eux-memes le passage en jeu (niveau 0 -> 1) ; Jonne (releve
        // demande par l'invite), VehicleSync (table repondue aux cles de l'invite), Vin (table demandee par l'invite) ; et tout ce
        // qui est redit periodiquement de toute facon (Garage, Knobs, Tow, CutterHitch, FuelPrices, Wear, SleepingBags, Machines,
        // IceRace, Rally, Races, Radio, Ram). Les boulots (Jobs) ne partent qu'a leurs changements : l'arrivant les a par la
        // sauvegarde ; un recalage en masse (SnapshotTo) toucherait des automates a horloge par joueur (paies) -- pas sans tri.
        public static int SnapshotAll(Peer p, string why)
        {
            if (!Session.IsHost || p == null) return 0;
            int k = 0;
            Interactions.ScheduleSnapshot(p); k++;   // portes, interrupteurs, robinets... : booleens des automates suivis (12 s)
            Fluids.ScheduleSnapshot(p); k++;         // niveaux de tous les liquides (20 s)
            Consume.ScheduleSnapshot(p); k++;        // articles manges, bus, jetes ; contenu des boites (20 s)
            ItemFsms.ScheduleSnapshot(p); k++;       // objets portables : lampes, lanterne, chargeur... (relecture du 15/10, ecart n2 ; 20 s)
            WorldFsms.ScheduleSnapshot(p); k++;      // automates du monde : etat et variables (22 s)
            CarDoors.ScheduleSnapshot(p); k++;       // portieres, capots, coffres, banquettes (20 s)
            Garage.ScheduleSnapshot(p); k++;         // crics, pont, chargeur, degats : vitres, trains, toles (relecture du 15/10 ; 20 s)
            Knobs.ScheduleSnapshot(p); k++;          // boutons des voitures garees -- phares, essuie-glaces -- et commandes tenues (ecart n17 ; 20 s)
            TaxiCustomer.ScheduleSnapshot(p); k++;   // client du taxi : automates et apparence (20 s)
            Fires.ScheduleSnapshot(p); Cooking.ScheduleSnapshot(p); Frost.ScheduleSnapshot(p); k += 3;   // feux, cuisson, givre (25-28 s, comme a l'arrivee)
            Markers.ScheduleSnapshot(p); k++;        // marqueurs partages poses avant son arrivee (20 s)
            // A tous les invites (pas de destinataire) : boutons du vendeur du rallye vendus et saucisse en attente au kiosque
            // (ecart n14), pose des objets au repos, equipements de la 300D et bagages du taxi (revue du 14/10 : jamais redits a
            // celui qui arrive), tables des cles courtes des particules et des sons.
            Shop.SendSellers(why); k++;
            Props.ScheduleSnapshot(); k++;
            ModMachtwagen300D.ScheduleSnapshot(); k++;
            Jobs.LuggageResend(); k++;
            Particles.ScheduleSnapshot(); CopySounds.ScheduleSnapshot(); k += 2;
            return k;
        }

        // ---------------------------------------------------------------- resynchronisation (menu F10, onglet SYNCHRO)
        // Un invite redemande tout l'etat a l'hote (@resync, fiable) ; l'hote le lui renvoie comme a son arrivee. Chez l'hote,
        // le bouton renvoie tout a chaque invite en jeu (invites soupconnes d'avoir derive). Une fois toutes les 30 s par
        // joueur, des deux cotes.
        const float ResyncEvery = 30f;
        static float resyncAt = -1f;                                                     // derniere demande d'ici (hote : dernier renvoi)
        static readonly Dictionary<int, float> resyncBy = new Dictionary<int, float>();  // hote : derniere demande de chaque invite
        static float resyncSince = -1f;                                                  // invite : heure de la demande (essai)
        static readonly Dictionary<Msg, int> resyncIn = new Dictionary<Msg, int>();      // invite : messages fiables recus depuis, par type

        // Secondes a attendre avant de pouvoir redemander (0 : tout de suite).
        public static float ResyncWait { get { return resyncAt < 0f ? 0f : Mathf.Max(0f, resyncAt + ResyncEvery - Time.realtimeSinceStartup); } }

        // Bouton « Resynchroniser le monde » : vrai si c'est parti.
        public static bool RequestResync()
        {
            if (!Session.Active || !InGame || ResyncWait > 0f) return false;
            float now = Time.realtimeSinceStartup;
            if (Session.IsHost)
            {
                int n = 0, k = 0;
                foreach (PlayerInfo pi in Session.Players.Values)
                    if (!pi.Local && pi.Level == 1 && pi.Peer != null) { k = SnapshotAll(pi.Peer, "resynchronisation par l'hote"); n++; }
                if (n == 0) return false;
                resyncAt = now;
                Log.Info("resynchronisation : etat complet renvoye a " + n + " invite(s) (" + k + " modules)");
                return true;
            }
            if (!Session.HostConnected) return false;
            resyncAt = now; resyncSince = now; resyncIn.Clear();
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@resync"), true);
            Log.Info("resynchronisation : demandee a l'hote");
            return true;
        }

        // Hote : demande @resync d'un invite (Jobs.OnSpecial).
        public static void OnResyncRequest(int who)
        {
            if (!Session.IsHost) return;
            PlayerInfo pi;
            if (!Session.Players.TryGetValue(who, out pi) || pi.Local || pi.Peer == null) return;
            float now = Time.realtimeSinceStartup, last;
            if (resyncBy.TryGetValue(who, out last) && now - last < ResyncEvery)
            {
                Log.Info("resynchronisation : demandee par #" + who + " (deja il y a " + (now - last).ToString("F0") + " s : ignoree)");
                return;
            }
            resyncBy[who] = now;
            Log.Info("resynchronisation : demandee par #" + who);
            int k = SnapshotAll(pi.Peer, "resynchronisation demandee par #" + who);
            Log.Info("resynchronisation : etat complet renvoye a #" + who + " (" + k + " modules)");
        }

        // Invite : message recu (a appeler de Session.Message, tous types) -- les fiables sont comptes pendant une
        // resynchronisation, pour l'essai « resync ».
        public static void NoteIncoming(Msg type)
        {
            if (resyncSince < 0f || !Session.InReliable) return;
            int n; resyncIn.TryGetValue(type, out n); resyncIn[type] = n + 1;
        }

        // [Test] Autotest=resync : l'invite demande la resynchronisation a 30 s (comme le bouton ; l'hote repond dans son
        // journal) ; a 45 s il note les messages fiables arrives depuis, par type (si Session.Message appelle NoteIncoming).
        static int testStep;
        public static void TestResync(float t)
        {
            if (Session.IsHost || !Session.Active) return;
            if (testStep == 0 && t > 30f) { testStep = 1; Log.Info("autotest : resync, demande " + (RequestResync() ? "envoyee" : "refusee (attente " + ResyncWait.ToString("F0") + " s)")); }
            if (testStep == 1 && t > 45f)
            {
                testStep = 2;
                int total = 0;
                var sb = new System.Text.StringBuilder();
                foreach (var kv in resyncIn) { total += kv.Value; sb.Append(sb.Length > 0 ? ", " : "").Append(kv.Key).Append(' ').Append(kv.Value); }
                Log.Info("autotest : resync, " + total + " message(s) fiable(s) recu(s) depuis la demande" + (total > 0 ? " : " + sb : " (rien compte : Session.Message n'appelle pas PlayerSync.NoteIncoming ?)"));
            }
        }

        static void RemoveAvatar(int id)
        {
            Avatar a;
            if (avatars.TryGetValue(id, out a)) { a.Destroy(); avatars.Remove(id); }
        }

        public static IEnumerable<Avatar> Avatars { get { return avatars.Values; } }
    }
}
