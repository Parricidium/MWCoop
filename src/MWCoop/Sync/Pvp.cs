using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Coups de poing entre joueurs (suggestion de квазарик, 13/10 : « tuer les autres joueurs a coups de poing, avec une option
    // de l'hote pour le PvP »). [Coop] PvP (lanceur : Options du jeu > Partie et partage ; coupe d'origine) : regle de l'hote,
    // dite aux invites (@pvp, comme les triches). Le coup du jeu (poing, releve par Gestures) : 0,15 s apres, l'avatar d'un
    // autre joueur a pied juste devant (moins de 1,4 m, dans un couloir de 45 cm) est touche -- @coup a ce joueur. Celui qui
    // est touche recule un peu ; 5 coups en moins de 20 s : mort (« meurtre »), et la reapparition de MWCoop comme toute mort.
    public static class Pvp
    {
        const int HitsToKill = 5;
        const float HitWindow = 20f;
        static bool hostAllows;
        static float nextAnnounce, punchAt = -1f, lastHitAt = -100f;
        static int hits;
        static Vector3 push; static float pushUntil;

        public static bool Allowed { get { return Session.Active && (Session.IsHost ? Config.GetInt("Coop", "PvP", 0) != 0 : hostAllows); } }

        public static void Update()
        {
            if (!Session.Active) return;
            float now = Time.realtimeSinceStartup;
            if (Session.IsHost && Session.RemoteCount > 0 && now >= nextAnnounce)
            {
                nextAnnounce = now + 10f;
                Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@pvp").Bool(Allowed), true);
            }
            if (punchAt > 0f && now >= punchAt) { punchAt = -1f; TryHit(); }
            if (now < pushUntil)
            {
                Transform pl = Game.PlayerT;
                var cc = pl != null ? pl.GetComponent<CharacterController>() : null;
                if (cc != null && cc.enabled) cc.Move(push * Time.deltaTime);
            }
        }

        // Gestures : le poing vient de partir ici.
        public static void OnLocalPunch()
        {
            if (Allowed && PlayerSync.InGame && VehicleSync.LocalDriving < 0 && !Seats.Seated) punchAt = Time.realtimeSinceStartup + 0.15f;
        }

        static void TryHit()
        {
            Transform cam = PlayerSync.LocalCamera;
            if (cam == null) return;
            int best = -1;
            float bd = 1.4f;
            foreach (PlayerInfo pi in Session.Players.Values)
            {
                if (pi.Local || pi.Level != 1) continue;
                if (VehicleSync.RemoteCarName(pi.Id) != null || Seats.RemoteSeatIndex(pi.Id) >= 0) continue;   // (en voiture : pas atteignable)
                foreach (float h in new[] { 0.9f, 1.3f, 1.6f })   // (ventre, poitrine, tete)
                {
                    Vector3 p = pi.State.Feet + Vector3.up * h - cam.position;
                    float along = Vector3.Dot(p, cam.forward);
                    if (along < 0.1f || along > bd) continue;
                    if ((p - cam.forward * along).magnitude > 0.45f) continue;
                    bd = along; best = pi.Id;
                }
            }
            if (best < 0) return;
            Vector3 dir = cam.forward; dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward;
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@coup").U8(best).Vec(dir), true);
            Log.Info("pvp : coup de poing sur #" + best + " (" + bd.ToString("F2") + " m)");
        }

        // [Test] Autotest=pvp (hote avec [Coop] PvP=1) : a 30 s l'invite se pose 0,9 m devant l'hote, face a lui ; de 40 a 47 s
        // l'hote se tourne vers lui et donne un coup par seconde. Attendu chez l'invite : « frappe par » 1/5 a 4/5, puis la mort.
        static int testStep; static float testNext;
        public static void Test(string mode, float t)
        {
            if (mode != "pvp") return;
            PlayerInfo other = null;
            foreach (PlayerInfo pi in Session.Players.Values) if (!pi.Local && pi.Level == 1) { other = pi; break; }
            Transform me = Game.PlayerT;
            if (other == null || me == null) return;
            if (!Session.IsHost && testStep == 0 && t > 30f)
            {
                testStep = 1;
                Vector3 f = Quaternion.Euler(0f, other.State.Yaw, 0f) * Vector3.forward;
                var cc = me.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                me.position = other.State.Feet + f * 0.9f + Vector3.up * 0.1f;
                me.rotation = Quaternion.LookRotation(-f, Vector3.up);
                if (cc != null) cc.enabled = true;
                Log.Info("autotest : pvp, invite pose devant l'hote");
            }
            if (Session.IsHost && t > 40f && t < 47f && Time.realtimeSinceStartup >= testNext)
            {
                testNext = Time.realtimeSinceStartup + 1f;
                Vector3 d = other.State.Feet - me.position; d.y = 0f;
                if (d.sqrMagnitude > 1e-4f) { me.rotation = Quaternion.LookRotation(d, Vector3.up); if (d.magnitude > 1f) me.position += d.normalized * (d.magnitude - 0.9f); }   // (il avance vers lui)
                Transform cam = PlayerSync.LocalCamera;
                if (cam != null) cam.localRotation = Quaternion.identity;
                Log.Info("autotest : pvp, coup (invite a " + d.magnitude.ToString("F2") + " m, autorise " + Allowed + ")");
                OnLocalPunch();
            }
        }

        public static void OnAllowed(bool on)
        {
            if (on != hostAllows) Log.Info("pvp : coups de poing " + (on ? "autorises" : "coupes") + " par l'hote");
            hostAllows = on;
        }

        public static void OnPunch(int who, NetReader r)
        {
            int target = r.U8();
            Vector3 dir = r.Vec();
            if (target != Session.LocalId || !Allowed || !PlayerSync.InGame || Respawn.Choosing) return;
            float now = Time.realtimeSinceStartup;
            hits = now - lastHitAt > HitWindow ? 1 : hits + 1;
            lastHitAt = now;
            push = dir * 1.2f; pushUntil = now + 0.15f;   // (~20 cm)
            PlayerInfo from;
            string name = Session.Players.TryGetValue(who, out from) ? from.Name : "#" + who;
            Log.Info("pvp : frappe par " + name + " (" + hits + "/" + HitsToKill + ")");
            if (hits < HitsToKill) { Hud.Toast(Lang.T("Coup de poing de ", "Punched by ") + name + " (" + hits + "/" + HitsToKill + ")"); return; }
            hits = 0;
            Log.Info("pvp : " + Respawn.TestDie("Murder"));
        }
    }
}
