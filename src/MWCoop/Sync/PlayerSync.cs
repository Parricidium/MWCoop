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
            F_Jacket = 1024, F_Coverall = 2048, F_Helmet = 4096;   // vetements portes (Wear) : caches chez les autres, montres par l'avatar
        const float SendRate = 1f / 20f;
        static float nextSend;
        static Transform player, cam, smoking, drinking, hello;
        public static Transform LocalCamera { get { return cam; } }
        // Pieds du joueur local (dernier etat envoye).
        public static Vector3 LocalFeet { get { return Session.Me != null ? Session.Me.State.Feet : Vector3.zero; } }
        static CharacterController controller;
        static PlayMakerFSM crouchFsm, smokeFsm;
        static readonly Dictionary<int, Avatar> avatars = new Dictionary<int, Avatar>();

        static PlayerSync()
        {
            Session.PlayerLeft += pi => { RemoveAvatar(pi.Id); Seats.PlayerLeft(pi.Id); Gestures.PlayerLeft(pi.Id); Stock.PlayerLeft(pi.Id); CarDoors.PlayerLeft(pi.Id); };
        }

        public static bool InGame { get { return Application.loadedLevelName == "GAME"; } }

        public static void OnLevelLoaded()
        {
            player = cam = null; crouchFsm = null; smokeFsm = null;
            controller = null;
            foreach (Avatar a in avatars.Values) a.Destroy();
            avatars.Clear();
            Avatar.ResetTemplate();
        }

        static bool FindPlayer()
        {
            if (player != null) return true;
            GameObject go = GameObject.Find("PLAYER");
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
                a.Apply(pi);
            }
        }

        // Apres les animations : poses calculees (conduite, buste et tete qui suivent le regard, bras).
        public static void LateUpdate()
        {
            if (!InGame) return;
            foreach (Avatar a in avatars.Values) a.LatePose();
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
            var st = new PlayerState();
            if (InGame && FindPlayer())
            {
                float h = controller != null ? controller.height : 1.8f;
                Vector3 center = controller != null ? player.TransformPoint(controller.center) : player.position;
                st.Feet = center - Vector3.up * h * 0.5f;
                st.Head = cam.position;
                // Regard : direction de la camera (le corps du joueur ne tourne pas toujours avec elle).
                Vector3 fw = cam.forward;
                Vector3 flat = new Vector3(fw.x, 0f, fw.z);
                st.Yaw = flat.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(flat).eulerAngles.y : player.eulerAngles.y;
                st.Pitch = -Mathf.Asin(Mathf.Clamp(fw.y, -1f, 1f)) * Mathf.Rad2Deg;
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
                st.Flags |= Wear.LocalFlags;
                st.Flags |= Config.GetInt("Test", "TestFlags", 0) | Autotest.PoseFlags;   // essais : postures forcees
                if ((st.Flags & F_Drink) != 0 && st.Drink == 0) st.Drink = Config.GetInt("Test", "TestBoisson", 0);   // (essais : quelle boisson)
            }
            Session.Me.State = st;
            var w = new NetWriter(Msg.PlayerState).U8(Session.LocalId).U8(Session.Me.Level)
                .Vec(st.Feet).Vec(st.Head).F32(st.Yaw).F32(st.Pitch).F32(st.Height).F32(st.Speed).U16(st.Flags).U8(st.Drink);
            Session.SendAll(w, false);
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
                if (levelChanged && level == 1)
                {
                    // Arrive en jeu : etat des portes, liquides et objets consommes pendant son chargement.
                    Interactions.ScheduleSnapshot(from);
                    Fluids.ScheduleSnapshot(from);
                    Consume.ScheduleSnapshot(from);
                    WorldFsms.ScheduleSnapshot(from);
                    CarDoors.ScheduleSnapshot(from);
                }
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
