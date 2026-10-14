using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Renverse par un vehicule (suggestions de Menor et Somegames1, 12/10 : « se faire renverser par son ami », « l'invite
    // n'est jamais renverse par les PNJ »). Le jeu ne tue que le joueur LOCAL, par une vraie collision entre une voiture
    // physique et son CharacterController (automate 'Throttle' des voitures PNJ : CollisionEvent tag Player, vitesse
    // relative > 5 m/s -> Systems/Death, RunOver). Or l'avatar d'un autre joueur n'a pas de collisionneur (Avatar.Strip),
    // et chez l'invite les voitures PNJ sont des copies cinematiques muettes (Traffic.Mute) : personne d'autre que l'hote
    // a pied ne pouvait mourir ainsi.
    //  - La ou la voiture est physique (son conducteur ; l'hote pour la circulation PNJ), on regarde si sa carrosserie
    //    traverse un avatar (point au bassin et a la poitrine, balayage le long du deplacement) au-dessus de [Coop]
    //    VitesseRenverse km/h (20) ; si oui, @renverse a la victime (une fois par 5 s), qui meurt chez elle (RunOver, la
    //    reapparition s'en charge) ; tous gardent l'elan de la voiture pour le pantin de la victime.
    //  - Pas de collisionneur sur les avatars (les voitures buteraient contre les amis, les copies cinematiques feraient
    //    s'envoler les voitures) : des requetes de recouvrement seulement.
    //  [Coop] Renverser=0 : coupe (ni detection ni mort).
    public static class RunOver
    {
        static readonly Dictionary<int, float> sent = new Dictionary<int, float>();
        static readonly Dictionary<int, KeyValuePair<Vector3, float>> hits = new Dictionary<int, KeyValuePair<Vector3, float>>();
        static int logs;

        static bool On { get { return Config.GetInt("Coop", "Renverser", 1) != 0; } }
        static float MinKmh { get { return Mathf.Clamp(Config.GetFloat("Coop", "VitesseRenverse", 20f), 5f, 200f); } }

        public static void OnLevelLoaded() { sent.Clear(); hits.Clear(); }

        // Vehicule physique ici (conduit, mene, ou PNJ chez l'hote) : renverse-t-il un autre joueur ?
        public static void Check(Transform root, Rigidbody body, string carName)
        {
            if (!On || !Session.Active || root == null || body == null || body.isKinematic) return;
            Vector3 v = body.velocity;
            float kmh = v.magnitude * 3.6f;
            if (kmh < MinKmh) return;
            Vector3 com = body.worldCenterOfMass;
            float now = Time.realtimeSinceStartup;
            foreach (Avatar a in PlayerSync.Avatars)
            {
                if (a == null || a.Player == null || a.Root == null) continue;
                PlayerState st = a.Player.State;
                if (a.InVehicle || a.Ragdolled || a.Player.Level != 1) continue;
                if ((st.Flags & (PlayerSync.F_Dead | PlayerSync.F_Seated | PlayerSync.F_Sleep)) != 0) continue;
                Vector3 feet = st.Feet;
                if ((feet - com).sqrMagnitude > 10f * 10f) continue;
                float last;
                if (sent.TryGetValue(a.Player.Id, out last) && now - last < 5f) continue;
                if (!Touches(root, feet + Vector3.up * 0.55f, v) && !Touches(root, feet + Vector3.up * 1.2f, v)) continue;
                sent[a.Player.Id] = now;
                Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@renverse").U8(a.Player.Id).Str("RunOver").Vec(v).Str(carName ?? root.name), true);
                Hit(a.Player.Id, v);
                Log.Info("renverse : " + a.Player.Name + " (#" + a.Player.Id + ") par " + (carName ?? root.name) + " a " + kmh.ToString("F0") + " km/h");
            }
        }

        // Voiture de la circulation dont l'INVITE a pris la physique (Traffic.Claim, pour un choc possible) : chez l'hote elle
        // n'est plus testee (pas a lui), et chez l'invite l'automate du jeu qui tue au contact (Throttle) est coupe sur les
        // copies -- personne ne voyait le choc (Menor, 12/10 : « the client don't get run over by npcs »). On teste ici le
        // joueur LOCAL ; les autres recoivent @renverse pour l'elan du pantin.
        public static void CheckLocal(Transform root, Rigidbody body, string carName)
        {
            if (!On || !Session.Active || root == null || body == null || body.isKinematic || Session.Me == null) return;
            Vector3 v = body.velocity;
            float kmh = v.magnitude * 3.6f;
            if (kmh < MinKmh) return;
            if (Respawn.Choosing || Seats.Seated || VehicleSync.LocalDriving >= 0) return;
            PlayerState st = Session.Me.State;
            if ((st.Flags & (PlayerSync.F_Dead | PlayerSync.F_Seated | PlayerSync.F_Sleep)) != 0) return;
            Vector3 feet = st.Feet;
            if ((feet - body.worldCenterOfMass).sqrMagnitude > 10f * 10f) return;
            float now = Time.realtimeSinceStartup, last;
            if (sent.TryGetValue(Session.LocalId, out last) && now - last < 5f) return;
            if (!Touches(root, feet + Vector3.up * 0.55f, v) && !Touches(root, feet + Vector3.up * 1.2f, v)) return;
            sent[Session.LocalId] = now;
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@renverse").U8(Session.LocalId).Str("RunOver").Vec(v).Str(carName ?? root.name), true);
            Hit(Session.LocalId, v);
            Log.Info("renverse ici par " + (carName ?? root.name) + " (physique prise, " + kmh.ToString("F0") + " km/h) : " + Respawn.TestDie("RunOver"));
        }

        // Carrosserie de 'root' sur le point (ou passee dessus depuis la derniere mesure, 0,05 s : balayage a rebours).
        static bool Touches(Transform root, Vector3 p, Vector3 v)
        {
            foreach (Collider c in Physics.OverlapSphere(p, 0.4f))
                if (c != null && !c.isTrigger && c.transform.IsChildOf(root)) return true;
            float d = v.magnitude * 0.06f;
            if (d > 0.05f)
                foreach (RaycastHit h in Physics.SphereCastAll(p, 0.35f, -v.normalized, d))
                    if (h.collider != null && !h.collider.isTrigger && h.collider.transform.IsChildOf(root)) return true;
            return false;
        }

        static void Hit(int id, Vector3 v) { hits[id] = new KeyValuePair<Vector3, float>(v, Time.realtimeSinceStartup); }

        // Elan donne au pantin de 'id' (renverse il y a moins de 2 s), sinon faux.
        public static bool Impulse(int id, out Vector3 v)
        {
            KeyValuePair<Vector3, float> h;
            v = Vector3.zero;
            if (!hits.TryGetValue(id, out h) || Time.realtimeSinceStartup - h.Value > 2f) return false;
            v = h.Key * 0.7f + Vector3.up * 2.5f;
            return true;
        }

        // [Test] Autotest=renverse (2 joueurs) : 40 s apres l'arrivee de l'autre, l'invite se pose a pied 9 m derriere la
        // SORBET ; l'hote monte au volant et fonce vers lui a 8 m/s de +6 a +10 s. Chacun note ce qu'il voit.
        static int tStep; static float tAt;
        public static void Test(string mode, float t)
        {
            if (mode != "renverse") return;
            float now = Time.realtimeSinceStartup;
            if (tStep == 0 && Jobs.OtherInGame(40f)) { tStep = 1; tAt = now; }
            if (tStep == 0) return;
            Rigidbody car = VehicleSync.Body("SORBET(190-200psi)");
            if (car == null) return;
            Vector3 back = -car.transform.forward; back.y = 0; back.Normalize();
            if (!Session.IsHost)
            {
                if (tStep == 1)
                {
                    tStep = 2;
                    GameObject pl = Game.PlayerGo;
                    var cc = pl != null ? pl.GetComponent<CharacterController>() : null;
                    if (cc != null) cc.enabled = false;
                    if (pl != null) pl.transform.position = car.position + back * 9f + Vector3.up * 0.5f;
                    if (cc != null) cc.enabled = true;
                    Log.Info("autotest : renverse, invite a pied derriere la SORBET");
                }
                if (tStep == 2 && now - tAt > 14f) { tStep = 3; Log.Info("autotest : renverse, invite : " + Respawn.State()); }
                return;
            }
            if (tStep == 1 && now - tAt > 1f) { tStep = 2; Log.Info("autotest : renverse, " + VehicleSync.TestEnter("SORBET(190-200psi)", false)); }
            if (tStep == 2 && now - tAt > 4f) { tStep = 3; Log.Info("autotest : renverse, volant " + VehicleSync.TestEnter("SORBET(190-200psi)", true)); }
            if (tStep == 3 && now - tAt > 6f && now - tAt < 10f)
            {
                Avatar target = null;
                foreach (Avatar a in PlayerSync.Avatars) if (a != null && a.Root != null) target = a;
                Vector3 d = (target != null ? target.Player.State.Feet : car.position + back * 9f) - car.position; d.y = 0;
                car.velocity = d.normalized * 8f + Vector3.up * Mathf.Min(car.velocity.y, 0f);
            }
            if (tStep == 3 && now - tAt > 12f)
            {
                tStep = 4;
                foreach (Avatar a in PlayerSync.Avatars) if (a != null && a.Root != null) Log.Info("autotest : renverse, hote voit #" + a.Player.Id + (a.Ragdolled ? " en pantin" : " debout") + ", pieds " + a.Player.State.Feet.ToString("F1"));
            }
        }

        public static void OnMessage(int who, NetReader r)
        {
            int victim = r.U8();
            string cause = r.Str();
            Vector3 v = r.Vec();
            string car = r.More ? r.Str() : "?";
            Hit(victim, v);
            if (victim != Session.LocalId || !On) return;
            if (Respawn.Choosing || Seats.Seated || VehicleSync.LocalDriving >= 0) return;
            if (logs++ < 10) Log.Info("renverse par " + car + " (#" + who + ", " + (v.magnitude * 3.6f).ToString("F0") + " km/h) : " + Respawn.TestDie(cause == "RunOverRally" ? cause : "RunOver"));
            else Respawn.TestDie("RunOver");
        }
    }
}
