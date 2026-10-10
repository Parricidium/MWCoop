using System;
using System.Reflection;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Compatibilite du mod « Machtwagen 300D » (Homura et Bogle911 ; retour de joueurs, 10/10 : « on n'arrive pas a refermer
    // les portes »). Le mod copie le taxi (« Machtwagen300D ») et ajoute un verrouillage centralise (CentralLocking) : il met
    // DoorLockedCheck en tete de l'etat « Open door » de chaque portiere (et de la trappe a essence) puis, verrouille, coupe
    // les 8 actions qui suivent (Actions[1..8] ; 4 pour la trappe). Deux soucis en coop :
    //  - CarDoors ajoute aussi son action en tete de « Open door » : les numeros glissent d'un cran -- le mod coupait sa propre
    //    verification et laissait une action du jeu, la porte a moitie bloquee ;
    //  - le verrouillage restait a celui qui verrouille : chez lui les portes ouvertes par les autres ne bougeaient plus.
    // Ici : l'etat verrouille est envoye a chaque changement et par l'hote toutes les 10 s (@300d), applique ailleurs
    // (LockDoors / UnlockDoors du mod), et les actions sont remises d'apres la place reelle de DoorLockedCheck.
    public static class ModMachtwagen300D
    {
        static Component locking;
        static FieldInfo fLocked;
        static float nextLook, nextHost;
        static bool last, known, applying;

        public static void OnLevelLoaded() { locking = null; known = false; nextLook = 0f; }

        static bool Find()
        {
            if (locking != null) return true;
            if (Time.realtimeSinceStartup < nextLook) return false;
            nextLook = Time.realtimeSinceStartup + 5f;
            foreach (MonoBehaviour m in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
                if (m != null && m.GetType().Name == "CentralLocking" && m.GetType().Namespace == "Machtwagen300D_MWC") { locking = m; break; }
            if (locking == null) return false;
            fLocked = locking.GetType().GetField("doorsLocked");
            Log.Info("mods : Machtwagen 300D present, verrouillage centralise synchronise");
            return fLocked != null;
        }

        static bool Locked { get { return fLocked != null && locking != null && (bool)fLocked.GetValue(locking); } }

        // Actions coupees par le verrouillage, d'apres la place de DoorLockedCheck dans l'etat (et non Actions[1..n]).
        static void Fix()
        {
            bool on = !Locked;
            foreach (string name in new[] { "flState", "frState", "rlState", "rrState", "fuelState" })
            {
                FieldInfo fi = locking.GetType().GetField(name);
                FsmState s = fi != null ? fi.GetValue(locking) as FsmState : null;
                if (s == null || s.Actions == null) continue;
                int at = -1;
                for (int i = 0; i < s.Actions.Length; i++) if (s.Actions[i] != null && s.Actions[i].GetType().Name == "DoorLockedCheck") { at = i; break; }
                if (at < 0) continue;
                int n = name == "fuelState" ? 4 : 8;
                for (int i = 0; i < s.Actions.Length; i++)
                {
                    if (s.Actions[i] == null) continue;
                    if (i <= at) s.Actions[i].Enabled = true;                 // (actions d'avant -- celle de CarDoors -- et la verification)
                    else if (i <= at + n) s.Actions[i].Enabled = on;
                }
            }
        }

        public static void Update()
        {
            if (!Session.Active || !PlayerSync.InGame || !Find()) return;
            bool l = Locked;
            float now = Time.realtimeSinceStartup;
            if (!known || l != last)
            {
                bool changed = known;
                known = true; last = l;
                Fix();
                if (changed && !applying)
                {
                    Log.Info("mods : Machtwagen 300D " + (l ? "verrouillee" : "deverrouillee") + " ici");
                    Send(l, true);
                }
            }
            if (Session.IsHost && now >= nextHost) { nextHost = now + 10f; Send(l, false); }
        }

        static void Send(bool l, bool reliable) { Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@300d").Bool(l), reliable); }

        public static void OnRemote(int who, bool l)
        {
            if (!Find() || Locked == l) return;
            applying = true;
            try
            {
                MethodInfo m = locking.GetType().GetMethod(l ? "LockDoors" : "UnlockDoors");
                if (m != null) m.Invoke(locking, new object[] { true });
            }
            catch (Exception e) { Log.Warn("mods : Machtwagen 300D : " + (e.InnerException ?? e).Message); }
            finally { applying = false; }
            last = Locked; known = true;
            Fix();
            Log.Info("mods : Machtwagen 300D " + (l ? "verrouillee" : "deverrouillee") + " par #" + who);
        }
    }
}
