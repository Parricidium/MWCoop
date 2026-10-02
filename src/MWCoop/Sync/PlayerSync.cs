using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Position, regard et posture de chaque joueur ; un avatar (PNJ du jeu) par joueur distant.
    public static class PlayerSync
    {
        const float SendRate = 1f / 20f;
        static float nextSend;
        static Transform player, cam;
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

        static void SendLocal()
        {
            var st = new PlayerState();
            if (InGame && FindPlayer())
            {
                float h = controller != null ? controller.height : 1.8f;
                Vector3 center = controller != null ? player.TransformPoint(controller.center) : player.position;
                st.Feet = center - Vector3.up * h * 0.5f;
                st.Yaw = player.eulerAngles.y;
                float pitch = cam.localEulerAngles.x;
                st.Pitch = pitch > 180f ? pitch - 360f : pitch;
                st.Height = h;
                st.Speed = controller != null ? new Vector3(controller.velocity.x, 0, controller.velocity.z).magnitude : 0f;
                // Accroupi : hauteur nettement sous la hauteur debout (la plus grande vue).
                if (h > standHeight) standHeight = h;
                if (h < standHeight * 0.8f) st.Flags |= 1;
            }
            Session.Me.State = st;
            var w = new NetWriter(Msg.PlayerState).U8(Session.LocalId).U8(Session.Me.Level)
                .Vec(st.Feet).F32(st.Yaw).F32(st.Pitch).F32(st.Height).F32(st.Speed).U8(st.Flags);
            Session.SendAll(w, false);
        }

        public static void OnState(Peer from, NetReader r, byte[] raw, int off, int len)
        {
            int id = r.U8();
            if (Session.IsHost) id = from.Id;          // un invite ne parle que pour lui-meme
            PlayerInfo pi;
            if (!Session.Players.TryGetValue(id, out pi) || pi.Local) return;
            int level = r.U8();
            var st = new PlayerState { Feet = r.Vec(), Yaw = r.F32(), Pitch = r.F32(), Height = r.F32(), Speed = r.F32(), Flags = r.U8() };
            bool levelChanged = pi.Level != level;
            pi.Level = level;
            pi.State = st;
            pi.StateTime = Time.realtimeSinceStartup;
            if (Session.IsHost)
            {
                // Relais aux autres invites, avec le bon numero de joueur.
                var w = new NetWriter(Msg.PlayerState).U8(id).U8(level)
                    .Vec(st.Feet).F32(st.Yaw).F32(st.Pitch).F32(st.Height).F32(st.Speed).U8(st.Flags);
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
