using HutongGames.PlayMaker;
using UnityEngine;

namespace MWCoop
{
    // Essais : [Test] Autotest=trichessonde -- les automates de la meteo et de la prison (etats, actions, transitions) ;
    // Autotest=trichelieux -- ce qu'il y a sous chaque lieu de teleportation (rayons depuis le ciel autour du centre).
    public static class CheatsProbe
    {
        static bool done;
        public static void Test(string mode, float t)
        {
            if (mode == "trichelieux" && !done && t > 25f) { done = true; Places(); return; }
            if (mode != "trichessonde" || done || t < 25f) return;
            done = true;
            foreach (string[] p in new[] { new[] { "MAP/WEATHER/Clouds", "Weather" }, new[] { "MAP/WEATHER/Forecast", "Logic" }, new[] { "JAIL/Functions", "Time" }, new[] { "Systems/PlayerWanted", "Activate" } })
            {
                GameObject go = Game.FindAny(p[0]);
                PlayMakerFSM f = go != null ? Game.FsmOn(go, p[1]) : null;
                if (f == null) { Log.Info("sonde : " + p[0] + "::" + p[1] + " introuvable"); continue; }
                Log.Info("sonde : " + p[0] + "::" + p[1] + " [" + f.ActiveStateName + "] actif " + go.activeInHierarchy + "/" + f.enabled + " initialise " + f.Fsm.Initialized);
                if (!f.Fsm.Initialized) continue;
                foreach (FsmState s in f.Fsm.States)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (FsmTransition tr in s.Transitions) sb.Append(' ').Append(tr.EventName).Append("->").Append(tr.ToState);
                    Log.Info("sonde :    " + Contenu.Dump(s) + " ||" + sb);
                }
                foreach (FsmTransition tr in f.Fsm.GlobalTransitions) Log.Info("sonde :    global " + tr.EventName + " -> " + tr.ToState);
            }
        }

        static void Places()
        {
            foreach (Cheats.Place p in Cheats.Places)
            {
                var sb = new System.Text.StringBuilder("lieux : " + p.Fr + " " + p.At.ToString("F0") + " :");
                for (int i = 0; i < 9; i++)
                {
                    float a = i * 40f * Mathf.Deg2Rad, r = i == 0 ? 0f : 12f;
                    Vector3 top = new Vector3(p.At.x + Mathf.Cos(a) * r, p.At.y + 120f, p.At.z + Mathf.Sin(a) * r);
                    RaycastHit best = new RaycastHit(); bool any = false;
                    foreach (RaycastHit h in Physics.RaycastAll(top, Vector3.down, 400f))
                        if (h.collider != null && !h.collider.isTrigger && (!any || h.point.y > best.point.y)) { best = h; any = true; }
                    if (any) sb.Append(" | ").Append(i).Append(' ').Append(best.collider.name).Append(" y").Append(best.point.y.ToString("F1")).Append(" (").Append(Game.RootName(best.collider.transform)).Append(')');
                    else sb.Append(" | ").Append(i).Append(" rien");
                }
                Log.Info(sb.ToString());
            }
        }
    }
}
