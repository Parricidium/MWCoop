using System.Collections.Generic;
using UnityEngine;

namespace MWCoop
{
    // Corps en pose non finie. Essais du 05/11 : un NaN sur un corps physique gagne, parfois des le premier pas de physique, des voitures et des objets a des
    // kilometres, sans contact ; un corps simplement remis sur une bonne pose repart en NaN au pas suivant et reinfecte les autres ; NaN force sur un pantin : l'invite plante.
    // Verifie a chaque pas, AVANT la simulation, et juste avant une sauvegarde en jeu, la liste du releve des objets (Props.Scan, toutes les 10 s).
    //  - Pantin qui ne compte pas dans la sauvegarde : eteint chez ce joueur jusqu'au prochain chargement.
    //  - Autre objet : retire de la physique et remis sur sa derniere pose saine, automates non relances. Jamais eteint.
    //  - Voiture et ses pieces : pose et vitesse remises, et ses champs revus tout de suite par le garde-fou des voitures (Sanitize). Joueur : rien.
    // [Test] GardeNaN=0 : comme avant.
    public static class NanGuard
    {
        static Rigidbody[] bodies = new Rigidbody[0];
        static Vector3[] goodPos = new Vector3[0];
        static Quaternion[] goodRot = new Quaternion[0];
        static bool[] goodSet = new bool[0];
        static int mode = -1, cursor, step, logs, passLogs;
        static readonly List<Rigidbody> bad = new List<Rigidbody>();
        static readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();
        static int costOn = -1, costSteps; static float costLog;

        public static bool On { get { if (mode < 0) mode = Config.GetInt("Test", "GardeNaN", 1); return mode != 0; } }

        // Liste du releve de Props (pas de recherche de plus). Les poses saines d'un corps reste dans la liste sont gardees.
        public static void SetBodies(Rigidbody[] all)
        {
            var old = new Dictionary<Rigidbody, int>();
            for (int i = 0; i < bodies.Length; i++) if (goodSet[i] && !ReferenceEquals(bodies[i], null)) old[bodies[i]] = i;
            var p = new Vector3[all.Length]; var r = new Quaternion[all.Length]; var s = new bool[all.Length];
            for (int i = 0; i < all.Length; i++)
            {
                int o;
                if (old.TryGetValue(all[i], out o)) { p[i] = goodPos[o]; r[i] = goodRot[o]; s[i] = true; }
            }
            bodies = all; goodPos = p; goodRot = r; goodSet = s;
            if (cursor >= all.Length) cursor = 0;
        }

        public static void OnLevelLoaded() { bodies = new Rigidbody[0]; goodPos = new Vector3[0]; goodRot = new Quaternion[0]; goodSet = new bool[0]; cursor = 0; }

        static bool Sane(Vector3 v) { float x = v.x + v.y + v.z; return x - x == 0f && Mathf.Abs(v.x) < 1e5f && Mathf.Abs(v.y) < 1e5f && Mathf.Abs(v.z) < 1e5f; }

        // Au pas de physique (Core.FixedUpdate), avant la simulation.
        public static void Fixed()
        {
            if (!On) return;
            step++;
            if (costOn < 0) costOn = Config.GetInt("Test", "GardeCout", 0);
            if (costOn != 0) clock.Start();
            Check();
            // Poses saines : 8 corps par pas, tout le monde en 1 s environ pour 400 corps.
            Rigidbody[] list = bodies;
            for (int n = 0; n < 8 && list.Length > 0; n++)
            {
                if (cursor >= list.Length) cursor = 0;
                int i = cursor++;
                Rigidbody rb = list[i];
                if (rb == null) continue;
                Vector3 p = rb.position; Quaternion q = rb.rotation;
                float y = q.x + q.y + q.z + q.w;
                if (Sane(p) && y - y == 0f) { goodPos[i] = p; goodRot[i] = q; goodSet[i] = true; }
            }
            if (costOn != 0)
            {
                clock.Stop(); costSteps++;
                if (Time.realtimeSinceStartup >= costLog)
                {
                    costLog = Time.realtimeSinceStartup + 10f;
                    Log.Info("garde NaN : " + list.Length + " corps, " + (clock.Elapsed.TotalMilliseconds * 1000.0 / costSteps).ToString("F0") + " us par pas de physique (" + costSteps + " pas)");
                    clock.Reset(); costSteps = 0;
                }
            }
        }

        // Juste avant SAVEGAME (Game.SaveInPlace) : pas de pose non finie dans la sauvegarde.
        public static void BeforeSave() { if (On) Check(); }

        static void Check()
        {
            Rigidbody[] list = bodies;
            for (int i = 0; i < list.Length; i++)
            {
                Rigidbody rb = list[i];
                if (rb == null) continue;
                Vector3 p = rb.position;
                float x = p.x + p.y + p.z;
                // (fini et dans le monde : le cas de tous les corps, tous les pas. Fini mais au-dela de 1e5 apres avoir ete vu sain : projete -- jambes de
                // DrunkG2 a 1e14 m chez l'invite de Donskoy, 05/11, prises pour l'emplacement d'attente du jeu puis NaN ; jamais vu sain : objet range
                // par le jeu (cadeau de Noel de MSC a 7,8e31), laisse)
                if (x - x == 0f && (!goodSet[i] || (Mathf.Abs(p.x) < 1e5f && Mathf.Abs(p.y) < 1e5f && Mathf.Abs(p.z) < 1e5f))) continue;
                if (rb.gameObject.activeInHierarchy) bad.Add(rb);
            }
            if (bad.Count == 0) return;
            if (passLogs++ < 30)
            {
                var sb = new System.Text.StringBuilder("garde NaN : pas " + step + " (temps " + Time.fixedTime.ToString("F2") + "), " + bad.Count + " corps non finis :");
                for (int i = 0; i < bad.Count && i < 6; i++) sb.Append(' ').Append(bad[i].name).Append(';');
                Log.Warn(sb.ToString());
            }
            foreach (Rigidbody rb in bad) if (rb != null && rb.gameObject.activeInHierarchy) Strike(rb);
            bad.Clear();
        }

        static void Strike(Rigidbody rb)
        {
            if (Game.UnderPlayer(rb.transform)) return;
            Transform carRoot = VehicleSync.CarRoot(rb.transform);
            bool car = carRoot != null;
            Transform top = Top(rb.transform);
            Rigidbody[] chain = top.GetComponentsInChildren<Rigidbody>();
            PlayMakerFSM[] fsms = top.GetComponentsInChildren<PlayMakerFSM>(true);
            // Pantin : ses articulations relancent le calcul, et la vague part en general de lui. Compte dans la sauvegarde : traite comme un autre objet.
            if (!car && top.GetComponentInChildren<CharacterJoint>() != null && !Saved(fsms))
            {
                top.gameObject.SetActive(false);
                if (logs++ < 60) Log.Warn("garde NaN : pas " + step + " " + Recon.Path(top) + " non fini (pantin) : eteint ici jusqu'au prochain chargement (" + chain.Length + " corps)");
                return;
            }
            int fixedN = 0, moved = 0;
            // (Eteint puis rallume, seul moyen trouve de laver le corps : PlayMaker repartirait de l'etat de depart -- chargement, relecture de la sauvegarde. RestartOnEnable
            // coupe le temps de l'aller-retour.)
            bool[] restart = null; string[] before = null;
            if (!car)
            {
                restart = new bool[fsms.Length]; before = new string[fsms.Length];
                for (int k = 0; k < fsms.Length; k++) { restart[k] = fsms[k].Fsm.RestartOnEnable; before[k] = fsms[k].ActiveStateName; fsms[k].Fsm.RestartOnEnable = false; }
                top.gameObject.SetActive(false);
            }
            foreach (Rigidbody b in chain)
            {
                int i = System.Array.IndexOf(bodies, b);
                if (i < 0 || !goodSet[i]) continue;
                b.transform.position = goodPos[i]; b.transform.rotation = goodRot[i];
                if (car) { b.position = goodPos[i]; b.rotation = goodRot[i]; if (!b.isKinematic) { b.velocity = Vector3.zero; b.angularVelocity = Vector3.zero; } }
                fixedN++;
            }
            if (car) VehicleSync.SanitizeSoon(carRoot);
            else
            {
                top.gameObject.SetActive(true);
                foreach (Rigidbody b in chain) if (b != null && !b.isKinematic) { b.velocity = Vector3.zero; b.angularVelocity = Vector3.zero; }
                for (int k = 0; k < fsms.Length; k++) { fsms[k].Fsm.RestartOnEnable = restart[k]; if (fsms[k].ActiveStateName != before[k]) moved++; }
            }
            if (logs++ < 60) Log.Warn("garde NaN : pas " + step + " " + Recon.Path(rb.transform) + " non fini : " + (car ? "voiture, " : "retire et remis, ") + fixedN + "/" + chain.Length + " corps sur leur pose saine" + (moved > 0 ? ", " + moved + " automates ont change d'etat" : ""));
        }

        // Un automate de l'objet ecoute SAVEGAME : l'objet compte dans la sauvegarde.
        static bool Saved(PlayMakerFSM[] fsms)
        {
            foreach (PlayMakerFSM f in fsms)
            {
                if (f == null || f.Fsm == null) continue;
                foreach (HutongGames.PlayMaker.FsmTransition t in f.Fsm.GlobalTransitions) if (t.EventName == "SAVEGAME") return true;
            }
            return false;
        }

        // Plus haut parent qui porte encore un corps : le pantin entier, pas un membre.
        static Transform Top(Transform t)
        {
            Transform top = t;
            for (Transform x = t.parent; x != null; x = x.parent) if (x.GetComponent<Rigidbody>() != null) top = x;
            return top;
        }
    }
}
