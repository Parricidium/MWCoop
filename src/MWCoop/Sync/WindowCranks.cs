using HutongGames.PlayMaker;
using UnityEngine;

namespace MWCoop
{
    // Vitre du passager avant qui s'ouvre (Menor, 12/10 : « la vitre passager de la SORBET et du taxi, comme celle du
    // conducteur »). Le jeu n'a de manivelle que cote conducteur : Doors/DoorFront(leftx)/FrontL/opener, automate 'Knob'
    // (molette : State 1 descend la vitre -- iTween vers une pose de WindowPivot/GlassPivot --, State 2 la remonte, et tourne
    // la manivelle). Cote passager, la vitre a son pivot (FrontR/WindowPivot/GlassPivot) et une manivelle dessinee
    // (« opener 3 ») sans automate. Ici, 3 s apres le chargement (avant que Jobs n'accroche les commandes : la copie est alors
    // relevee et rejouee comme les autres, sous son propre chemin), la manivelle du conducteur est copiee a la place de celle
    // du passager, ses automates visant la vitre et la manivelle de droite (le pivot de la vitre de droite est deja en miroir : meme
    // mouvement local ; mesure, essai vitre : descente 0,32 m, x oppose, y et z egaux). Chez tous de la meme
    // facon. Portieres arriere : pas de pivot de vitre dans le jeu (rien a faire bouger).
    public static class WindowCranks
    {
        static float at = -1;
        static int logs;

        public static void OnLevelLoaded() { at = PlayerSync.InGame ? Time.realtimeSinceStartup + 3f : -1; }

        public static void Update()
        {
            if (at < 0 || Time.realtimeSinceStartup < at) return;
            at = -1;
            int n = 0;
            foreach (PlayMakerFSM f in Object.FindObjectsOfType<PlayMakerFSM>())
            {
                if (f == null || f.FsmName != "Knob" || f.gameObject.name != "opener" || f.transform.parent == null || f.transform.parent.name != "FrontL") continue;
                Transform doorL = f.transform.parent.parent;                     // DoorFront(leftx)
                Transform doors = doorL != null ? doorL.parent : null;
                Transform frontR = doors != null ? doors.Find("DoorFront(right)/FrontR") : null;
                Transform glassR = frontR != null ? frontR.Find("WindowPivot/GlassPivot") : null;
                if (glassR == null || frontR.Find("opener MWCoop") != null) continue;
                bool hasKnob = false;
                foreach (PlayMakerFSM k in frontR.GetComponentsInChildren<PlayMakerFSM>(true)) hasKnob |= k.FsmName == "Knob";
                if (hasKnob) continue;
                if (Make(f, frontR, glassR)) n++;
            }
            if (n > 0) Log.Info("vitres : manivelle passager ajoutee a " + n + " voiture(s)");
        }

        static bool Make(PlayMakerFSM knob, Transform frontR, Transform glassR)
        {
            try
            {
                Transform src = knob.transform, frontL = src.parent;
                Transform drawn = frontR.Find("opener 3") ?? (frontR.parent != null ? frontR.parent.Find("opener 3") : null);   // (SORBET : sous la portiere)
                var c = (GameObject)Object.Instantiate(src.gameObject);
                c.name = "opener MWCoop";
                c.transform.SetParent(frontR, false);
                // A la place de la manivelle dessinee (cachee), sinon en miroir de celle du conducteur.
                if (drawn != null) { c.transform.position = drawn.position; c.transform.rotation = drawn.rotation; drawn.gameObject.SetActive(false); }
                else { Vector3 lp = src.localPosition; c.transform.localPosition = new Vector3(-lp.x, lp.y, lp.z); c.transform.localRotation = src.localRotation; }
                Transform handle = c.transform.Find("Pivot");
                foreach (PlayMakerFSM f in c.GetComponents<PlayMakerFSM>())
                {
                    if (f.FsmName != "Knob") continue;
                    FsmGameObject w = f.FsmVariables.FindFsmGameObject("Windows"), h = f.FsmVariables.FindFsmGameObject("Handle");
                    if (w != null) w.Value = glassR.gameObject;
                    if (h != null && handle != null) h.Value = handle.gameObject;
                }
                if (logs++ < 4) Log.Info("vitres : manivelle passager de " + frontR.root.name + " (" + (drawn != null ? "a la place de opener 3" : "en miroir") + ")");
                return true;
            }
            catch (System.Exception e) { Log.Warn("vitres : manivelle passager : " + e.Message); return false; }
        }

        // [Test] Autotest=vitre (solo) : 25 s apres le chargement, la manivelle passager de la SORBET tourne 3 s (State 1, comme
        // la molette) ; la vitre de droite et celle de gauche (meme commande sur la manivelle du conducteur) sont notees, en
        // repere de la voiture : attendu, le meme mouvement en miroir.
        static int tStep; static Vector3 l0, r0; static Transform gl, gr, car; static PlayMakerFSM kl, kr;
        public static void Test(string mode, float t)
        {
            if (mode != "vitre") return;
            if (tStep == 0 && t > 25f)
            {
                GameObject s = GameObject.Find("SORBET(190-200psi)");
                if (s == null) return;
                car = s.transform;
                gl = car.Find("Doors/DoorFront(leftx)/FrontL/WindowPivot/GlassPivot"); gr = car.Find("Doors/DoorFront(right)/FrontR/WindowPivot/GlassPivot");
                Transform ol = car.Find("Doors/DoorFront(leftx)/FrontL/opener"), or = car.Find("Doors/DoorFront(right)/FrontR/opener MWCoop");
                if (gl == null || gr == null || ol == null || or == null) { tStep = 9; Log.Info("autotest : vitre, pieces introuvables (manivelle copiee " + (or != null) + ")"); return; }
                l0 = car.InverseTransformPoint(gl.position); r0 = car.InverseTransformPoint(gr.position);
                kl = Game.FsmOn(ol.gameObject, "Knob"); kr = Game.FsmOn(or.gameObject, "Knob");
                tStep = 1;
                Log.Info("autotest : vitre, les deux manivelles tournent");
            }
            if (tStep == 1 && t < 31f) foreach (PlayMakerFSM f in new[] { kl, kr }) if (f != null && f.ActiveStateName != "State 1") Game.SetState(f, "State 1");   // (molette tenue)
            if (tStep == 1 && t > 31f)
            {
                tStep = 2;
                Vector3 dl = car.InverseTransformPoint(gl.position) - l0, dr = car.InverseTransformPoint(gr.position) - r0;
                Log.Info("autotest : vitre, gauche " + dl.ToString("F3") + " ; droite " + dr.ToString("F3") + " (repere voiture ; attendu : x oppose, y et z egaux) ; suivies par Jobs : gauche " + Jobs.Tracks(kl) + ", droite " + Jobs.Tracks(kr));
            }
        }
    }
}
