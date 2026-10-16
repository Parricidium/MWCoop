using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Choc entre deux voitures de joueurs (Menor, 12/10 : « si tu percutes ton ami trop vite, par l'arriere ou autrement, il
    // meurt aussi, pas seulement toi »). Le jeu tue au choc par la liaison de la tete du conducteur (elle casse sous la
    // deceleration) -- chez chacun sur SA voiture. La voiture de l'autre est ici une copie cinematique : elle encaisse sans
    // rien sentir, et chez lui notre voiture en est une aussi. Ici : un temoin sur la voiture qu'on conduit note la derniere
    // voiture d'un autre joueur touchee (vitesse relative) ; si le jeu nous tue d'un accident dans la seconde qui suit
    // (Respawn.Intercept), son conducteur recoit @percute et meurt aussi -- sauf s'il etait attache et nous non (comme le
    // passager attache, OnAccident). Ses passagers suivent par son propre @accident.
    // Chocs notables de chaque voiture d'ici (temoin sur tous les corps de VehicleSync, vitesse relative > 4 m/s, mur, fosse ou
    // autre voiture) : Parts s'en sert pour admettre une rafale de casses dans les 2 s d'un vrai choc (16/10).
    public static class CarHits
    {
        public class Watch : MonoBehaviour
        {
            void OnCollisionEnter(Collision c) { Hit(transform, c); }
        }

        static Transform watched;
        static int lastId = -1;
        static string lastKey;
        static float lastAt = -10f, lastSpeed;
        static int logs, impactLogs;
        static readonly Dictionary<Transform, float> impacts = new Dictionary<Transform, float>();   // racine de voiture -> instant du dernier choc
        static float nextWatchAll;

        public static void OnLevelLoaded() { watched = null; lastId = -1; lastAt = -10f; impacts.Clear(); nextWatchAll = 0f; }

        public static void Update()
        {
            if (!Session.Active) return;
            float now = Time.realtimeSinceStartup;
            // Temoin de choc sur toutes les voitures d'ici (garee heurtee, copie recalee) : revues toutes les 2 s.
            if (now >= nextWatchAll)
            {
                nextWatchAll = now + 2f;
                for (int i = 0; i < VehicleSync.LocalCount; i++)
                {
                    Rigidbody b = VehicleSync.LocalBody(i);
                    if (b != null && b.GetComponent<Watch>() == null) b.gameObject.AddComponent<Watch>();
                }
            }
            Transform car = VehicleSync.LocalDrivingRoot;
            if (car == null || car == watched) return;
            watched = car;
            Rigidbody rb = car.GetComponent<Rigidbody>();
            if (rb != null && rb.GetComponent<Watch>() == null) rb.gameObject.AddComponent<Watch>();
        }

        // Secondes depuis le dernier choc notable de cette voiture (racine de VehicleSync) ; tres grand si aucun.
        public static float ImpactAgo(Transform carRoot)
        {
            float at;
            return carRoot != null && impacts.TryGetValue(carRoot, out at) ? Time.realtimeSinceStartup - at : 1e9f;
        }

        // Essais (autotest casse2) : un choc note a l'instant sur cette voiture.
        public static void NoteImpact(Transform carRoot) { if (carRoot != null) impacts[carRoot] = Time.realtimeSinceStartup; }

        static void Hit(Transform mine, Collision c)
        {
            if (c == null || c.collider == null) return;
            float speed = c.relativeVelocity.magnitude;
            if (speed > 4f)
            {
                Transform root = VehicleSync.CarRoot(mine);
                if (root == null) root = mine;
                impacts[root] = Time.realtimeSinceStartup;
                if (impactLogs++ < 10) Log.Info("choc : " + root.name + " heurte " + c.collider.name + " a " + (speed * 3.6f).ToString("F0") + " km/h");
            }
            if (mine != watched) return;   // (la derniere conduite : la mort coupe la conduite)
            Transform other = VehicleSync.CarRoot(c.collider.transform);
            if (other == null || other == mine) return;
            foreach (int id in Session.Players.Keys)
            {
                if (id == Session.LocalId) continue;
                Transform t = VehicleSync.RemoteCarTransform(id);
                if (t == null || (t != other && !t.IsChildOf(other) && !other.IsChildOf(t))) continue;
                lastId = id; lastKey = VehicleSync.KeyOf(other); lastAt = Time.realtimeSinceStartup; lastSpeed = c.relativeVelocity.magnitude;
                if (logs++ < 10) Log.Info("choc : voiture de #" + id + " (" + other.name + ") touchee a " + (lastSpeed * 3.6f).ToString("F0") + " km/h");
                if (Time.realtimeSinceStartup - crashAt < 0.5f) Send();   // (la tete a casse a la meme image, avant le choc note)
                return;
            }
        }

        // Mort d'accident ici (Respawn.Intercept) : la voiture d'un autre joueur touchee juste avant ?
        static float crashAt = -10f;
        static bool crashBelted;
        public static void OnLocalCrash(bool belted)
        {
            crashAt = Time.realtimeSinceStartup; crashBelted = belted;
            Send();
        }
        static void Send()
        {
            bool belted = crashBelted;
            if (lastId < 0 || lastKey == null || Time.realtimeSinceStartup - lastAt > 1.2f) return;
            crashAt = -10f;
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@percute").U8(lastId).Str(lastKey).Bool(belted).F32(lastSpeed), true);
            Log.Info("choc : mort apres avoir percute la voiture de #" + lastId + " (" + (lastSpeed * 3.6f).ToString("F0") + " km/h) : envoye");
            lastId = -1;
        }

        public static void OnMessage(int who, NetReader r)
        {
            int victim = r.U8();
            string key = r.Str();
            bool otherBelted = r.Bool();
            float speed = r.F32();
            if (victim != Session.LocalId || Respawn.Choosing || VehicleSync.LocalDrivingKey != key) return;
            bool belted = Game.GlobalBool("PlayerSeatbeltsOn");
            if (belted && !otherBelted) { Log.Info("choc : percute par #" + who + " (" + (speed * 3.6f).ToString("F0") + " km/h), attache ici : survit"); return; }
            Log.Info("choc : percute par #" + who + " (" + (speed * 3.6f).ToString("F0") + " km/h) : " + Respawn.TestDie("Crash"));
        }

        // [Test] Autotest=percute (2 joueurs) : l'invite au volant de [Test] TestVoiture2 (BACHGLOTZ), l'hote de [Test]
        // TestVoiture (SORBET) ; a 50 s l'hote pose la sienne 9 m derriere celle de l'invite et la lance a 15 m/s dedans ; a
        // 52 s, s'il l'a touchee, il meurt « d'accident » (comme si sa tete avait casse) : l'invite doit mourir aussi.
        static int tStep;
        public static void Test(string mode, float t)
        {
            if (mode != "percute") return;
            string mine = Session.IsHost ? Config.Get("Test", "TestVoiture", "SORBET(190-200psi)") : Config.Get("Test", "TestVoiture2", "BACHGLOTZ(1905kg)");
            if (tStep == 0 && t > 20f) { tStep = 1; Log.Info("autotest : percute, " + VehicleSync.TestEnter(mine, false)); }
            if (tStep == 1 && t > 26f) { tStep = 2; Log.Info("autotest : percute, volant " + VehicleSync.TestEnter(mine, true)); }
            if (!Session.IsHost) return;
            Rigidbody me = VehicleSync.Body(mine);
            // (le choc lui-meme : OnCollisionEnter de Unity ; teleporter la GIFU tue son conducteur -- ici le choc est note
            // tel qu'il le serait, contre la voiture de l'invite, puis la mort d'accident)
            if (tStep == 2 && t > 50f)
            {
                foreach (int id in Session.Players.Keys)
                {
                    if (id == Session.LocalId) continue;
                    Transform other = VehicleSync.RemoteCarTransform(id);
                    if (other == null) continue;
                    tStep = 3;
                    lastId = id; lastKey = VehicleSync.KeyOf(other); lastAt = Time.realtimeSinceStartup; lastSpeed = 15f;
                    Log.Info("autotest : percute, choc note contre " + other.name + " de #" + id + " : " + Respawn.TestDie("Crash"));
                }
                if (tStep == 2 && t > 60f) { tStep = 9; Log.Info("autotest : percute, pas de voiture de l'invite"); }
            }
        }
    }
}
