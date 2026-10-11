using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Fendeuse a bois du KEKMET (Cutter, automate 'Attach' de Cutter/PTOCheck) : attelee ("Attach 2" : son corps retire,
    // rattachee a KEKMET/HitchPivot/AttachPoint, CutterMass allume), detelee ("State 2" -> "State 1" : corps de 459 kg
    // rendu, a la racine). WorldFsms ne rejoue pas le detelage : "State 2" peut enchainer sur "PLR to driveshaft" (le joueur
    // accroche a l'arbre si la prise de force tourne), qui touche au joueur. Retour de ProTonChiK (11/10, 0.75.35) :
    // detelee chez l'hote, toujours attelee chez son ami ; l'ami prend le tracteur -- chez l'hote sa copie (masse infinie)
    // rentre dans la fendeuse posee par terre, tout s'envole. Ici : chaque changement attelee / detelee d'ici part aux
    // autres avec la pose de la fendeuse (@fendattel) ; ils la posent et mettent l'automate dans le meme etat ("Attach 2",
    // ou "State 1" pour le detelage -- jamais "State 2"). L'hote redit l'etat toutes les 10 s (arrivants, message perdu).
    public static class CutterHitch
    {
        static PlayMakerFSM f;
        static Transform cutter;
        static float scanAt = -1, nextLook, nextResend;
        static int last = -1;          // 1 attelee, 0 detelee (dernier etat vu ici)
        static bool applying;
        static float localAt = -100f;   // dernier changement fait ici : un etat redit par l'hote juste avant ne le defait pas

        public static void OnLevelLoaded()
        {
            f = null; cutter = null; last = -1;
            scanAt = PlayerSync.InGame ? Time.realtimeSinceStartup + 6f : -1;
        }

        static int Attached()
        {
            string s = f.ActiveStateName;
            return s == "Attach 2" || s == "Wait player 2" || s == "Wait button 2" || s == "State 2" ? 1 : 0;
        }

        public static void Update()
        {
            if (!Session.Active || scanAt < 0) return;
            float now = Time.realtimeSinceStartup;
            if (f == null)
            {
                if (now < scanAt) return;
                scanAt = now + 10f;
                GameObject pto = Game.FindAny("Cutter/PTOCheck");
                f = pto != null ? Game.FsmOn(pto, "Attach") : null;
                if (f == null) return;
                cutter = pto.transform.parent;
                Replay.Claim(f, "fendeuse");
                last = Attached();
                Log.Info("fendeuse : attelage suivi (" + (last == 1 ? "attelee" : "detelee") + ")");
            }
            if (now < nextLook || cutter == null) return;
            nextLook = now + 0.2f;
            int a = Attached();
            if (a != last)
            {
                last = a;
                if (!applying) { localAt = now; Send(a); Log.Info("fendeuse : " + (a == 1 ? "attelee" : "detelee") + " ici, envoye"); }
                applying = false;
            }
            if (Session.IsHost && Session.RemoteCount > 0 && now >= nextResend) { nextResend = now + 10f; Send(a); }
        }

        static void Send(int a)
        {
            if (Session.RemoteCount == 0) return;
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@fendattel").U8(a).Vec(cutter.position).Quat(cutter.rotation), true);
        }

        public static void OnRemote(int who, int a, Vector3 pos, Quaternion rot)
        {
            if (f == null || cutter == null || Attached() == a || Time.realtimeSinceStartup - localAt < 3f) return;
            applying = true;
            if (a == 1)
            {
                cutter.position = pos; cutter.rotation = rot;   // (ou elle est chez lui : contre la prise de force du tracteur)
                Game.SetState(f, "Attach 2");
            }
            else
            {
                Game.SetState(f, "State 1");
                cutter.position = pos; cutter.rotation = rot;
                Rigidbody rb = cutter.GetComponent<Rigidbody>();
                if (rb != null) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            }
            Log.Info("fendeuse : " + (a == 1 ? "attelee" : "detelee") + " par #" + who);
        }

        // Essais : etat ici, et pose de la fendeuse.
        public static string State()
        {
            if (f == null || cutter == null) return "fendeuse introuvable";
            return "fendeuse " + (Attached() == 1 ? "attelee" : "detelee") + " [" + f.ActiveStateName + "] sous " + (cutter.parent != null ? Recon.Path(cutter.parent) : "-")
                   + " en " + cutter.position.ToString("F1") + (cutter.GetComponent<Rigidbody>() != null ? ", corps" : ", sans corps");
        }
        public static string TestSet(int a)
        {
            if (f == null) return "fendeuse introuvable";
            if (a == 1)
            {
                Transform pto = Game.FindAny("KEKMET(350-400psi)/Simulation/PTOPivot") != null ? Game.FindAny("KEKMET(350-400psi)/Simulation/PTOPivot").transform : null;
                if (pto != null) cutter.position += pto.position - f.transform.position;   // (prise contre la prise de force)
                Game.SetState(f, "Attach 2");
            }
            else Game.SetState(f, "State 1");
            return "fendeuse mise " + (a == 1 ? "attelee" : "detelee") + " ici";
        }
    }
}
