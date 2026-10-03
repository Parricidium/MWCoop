using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Position, regard et posture de chaque joueur ; un avatar (PNJ du jeu) par joueur distant.
    public static class PlayerSync
    {
        // Posture et actions du joueur, montrees par son avatar (Avatar).
        public const int F_Crouch = 1, F_Seated = 2, F_Smoke = 4, F_Drink = 8, F_Carry = 16, F_Hello = 32, F_Sleep = 64;
        const float SendRate = 1f / 20f;
        static float nextSend;
        static Transform player, cam, smoking, drinking, hello;
        static CharacterController controller;
        static float standHeight;
        static readonly Dictionary<int, Avatar> avatars = new Dictionary<int, Avatar>();

        static PlayerSync()
        {
            Session.PlayerLeft += pi => RemoveAvatar(pi.Id);
        }

        public static bool InGame { get { return Application.loadedLevelName == "GAME"; } }

        public static void OnLevelLoaded()
        {
            player = cam = null;
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
            Transform c = player.Find("Pivot/AnimPivot/Camera/FPSCamera");
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

        static bool AnyChildActive(Transform t)
        {
            if (t == null || !t.gameObject.activeInHierarchy) return false;
            foreach (Transform c in t) if (c.gameObject.activeSelf) return true;
            return false;
        }

        static void SendLocal()
        {
            var st = new PlayerState();
            if (InGame && FindPlayer())
            {
                float h = controller != null ? controller.height : 1.8f;
                Vector3 center = controller != null ? player.TransformPoint(controller.center) : player.position;
                st.Feet = center - Vector3.up * h * 0.5f;
                st.Head = cam.position;
                st.Yaw = player.eulerAngles.y;
                float pitch = cam.localEulerAngles.x;
                st.Pitch = pitch > 180f ? pitch - 360f : pitch;
                st.Height = h;
                st.Speed = controller != null ? new Vector3(controller.velocity.x, 0, controller.velocity.z).magnitude : 0f;
                // Accroupi : hauteur nettement sous la hauteur debout (la plus grande vue).
                if (h > standHeight) standHeight = h;
                if (h < standHeight * 0.8f) st.Flags |= F_Crouch;
                if (Game.GlobalBool("PlayerSeated") || VehicleSync.LocalDriving >= 0) st.Flags |= F_Seated;
                if (Game.GlobalBool("PlayerSleeps")) st.Flags |= F_Sleep;
                if (smoking != null && smoking.gameObject.activeInHierarchy) st.Flags |= F_Smoke;
                if (AnyChildActive(drinking) || Game.GlobalBool("PlayerDrinkOn")) st.Flags |= F_Drink;
                if (hello != null && hello.gameObject.activeInHierarchy) st.Flags |= F_Hello;
                if (Props.Holding) st.Flags |= F_Carry;
                st.Flags |= Config.GetInt("Test", "TestFlags", 0) | Autotest.PoseFlags;   // essais : postures forcees
            }
            Session.Me.State = st;
            var w = new NetWriter(Msg.PlayerState).U8(Session.LocalId).U8(Session.Me.Level)
                .Vec(st.Feet).Vec(st.Head).F32(st.Yaw).F32(st.Pitch).F32(st.Height).F32(st.Speed).U8(st.Flags);
            Session.SendAll(w, false);
        }

        public static void OnState(Peer from, NetReader r, byte[] raw, int off, int len)
        {
            int id = r.U8();
            if (Session.IsHost) id = from.Id;          // un invite ne parle que pour lui-meme
            PlayerInfo pi;
            if (!Session.Players.TryGetValue(id, out pi) || pi.Local) return;
            int level = r.U8();
            var st = new PlayerState { Feet = r.Vec(), Head = r.Vec(), Yaw = r.F32(), Pitch = r.F32(), Height = r.F32(), Speed = r.F32(), Flags = r.U8() };
            bool levelChanged = pi.Level != level;
            pi.Level = level;
            pi.State = st;
            pi.StateTime = Time.realtimeSinceStartup;
            if (Session.IsHost)
            {
                // Relais aux autres invites, avec le bon numero de joueur.
                var w = new NetWriter(Msg.PlayerState).U8(id).U8(level)
                    .Vec(st.Feet).Vec(st.Head).F32(st.Yaw).F32(st.Pitch).F32(st.Height).F32(st.Speed).U8(st.Flags);
                Session.Broadcast(w, false, id);
                if (levelChanged) Session.SendRoster();
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
