using HutongGames.PlayMaker;
using UnityEngine;

namespace MWCoop
{
    // Dormir dans la chambre des parents (Maxim Pennis, 15/10 ; JD, 19/10 : oui). Le grand lit de la maison des parents
    // (YARD/Building/BEDROOM2 : bed_base/bed_soft) n'a pas de declencheur de sommeil ; celui du lit du joueur (BEDROOM1/
    // LOD_bedroom1/Sleep 1 : automate 'Activate' sur SleepTrigger, pivot anime AnimPivotSleep) est copie dessus : le corps dans
    // la longueur du lit, la tete du cote de la table de nuit, a la hauteur du matelas. Copie faite chez chacun, 14 s apres le
    // chargement ; dormir y fait comme dans son lit (temps, sauvegarde : l'automate du jeu). My Winter Car seulement. Corps remonte de 80 cm vers
    // la tete de lit (le milieu pivot-tete de l'animation n'est pas le milieu du corps : il depassait au pied du lit, 22/10).
    // [Test] LitParents=dx,dy,dz : decalage du corps (repere du monde : axe du lit, haut, travers), par defaut 0 / 0,08 / 0.
    public static class ParentsBed
    {
        static float at = -1; static bool done, aligned, releasing;
        static readonly System.Collections.Generic.List<Transform> pivots = new System.Collections.Generic.List<Transform>();
        static readonly System.Collections.Generic.List<Vector3> wakes = new System.Collections.Generic.List<Vector3>();   // (par place : ou se relever, sur le plancher)
        static Vector3 feetDir;   // (sens des pieds dans le lit, monde)
        static Transform lastPivot; static Quaternion savedLocal; static Vector3 wakeAt;

        public static void OnLevelLoaded() { at = PlayerSync.InGame && !Game.Msc ? Time.realtimeSinceStartup + 14f : -1; done = false; aligned = false; releasing = false; pivots.Clear(); wakes.Clear(); }

        public static void Update()
        {
            if (at < 0) return;
            if (!done)
            {
                if (Time.realtimeSinceStartup < at) return;
                done = true;
                try { Make(); } catch (System.Exception e) { Log.Error("lit des parents : " + e); }
                return;
            }
            // Couche dans ce lit : le jeu garde le cap qu'avait le joueur en s'allongeant (SetParent sans remise de la rotation :
            // il dormait en diagonale, JD 22/10 : « le corps doit suivre les bordures du lit ») -- cap pose dans l'axe du lit, les
            // pieds vers le bas du lit, une fois au debut du sommeil (les autres voient l'avatar couche dans ce sens).
            Transform pl = Game.PlayerT;
            if (pl == null) return;
            bool sleeps = Game.GlobalBool("PlayerSleeps");
            bool here = pl.parent != null && pivots.Contains(pl.parent) && sleeps;
            // Reveil (JD, 22/10 : « passe a travers le sol, reapparu dans la cuisine, camera au sol comme couche ») : le cap pose
            // ci-dessous est une rotation du joueur dans le monde, donc un changement de sa rotation LOCALE sous le pivot couche ;
            // quand l'animation de lever redresse le pivot, cette rotation locale devient un basculement (camera au sol). Et le
            // jeu repose le joueur a l'origine du declencheur, qui est sous le plancher ici (le pivot final est descendu sur le
            // matelas). Donc : rotation locale rendue des que le sommeil s'arrete (avant le lever), et joueur remis debout au
            // pied du lit, sur le plancher, des que le jeu le detache du pivot.
            if (aligned && !sleeps && pl.parent == lastPivot)
            {
                pl.localRotation = savedLocal;
                aligned = false; releasing = true;
                int i = pivots.IndexOf(lastPivot);
                wakeAt = i >= 0 && i < wakes.Count ? wakes[i] : lastPivot.position;
                Log.Info("lit des parents : fin du sommeil, rotation rendue ; lever prevu en " + wakeAt.ToString("F2"));
            }
            if (releasing && (pl.parent == null || !pivots.Contains(pl.parent)))
            {
                releasing = false;
                var cc = pl.GetComponent<CharacterController>();
                float feet = cc != null ? cc.center.y - cc.height / 2f : -0.9f;
                Vector3 to = new Vector3(wakeAt.x, wakeAt.y + 0.05f - feet, wakeAt.z);
                if (cc != null) cc.enabled = false;
                Vector3 was = pl.position;
                pl.position = to;
                pl.rotation = Quaternion.Euler(0f, Quaternion.LookRotation(-feetDir).eulerAngles.y, 0f);   // (debout, face au lit)
                if (cc != null) cc.enabled = true;
                Log.Info("lit des parents : leve, joueur remis en " + to.ToString("F2") + " (le jeu l'avait pose en " + was.ToString("F2") + ")");
            }
            if (here && !aligned && !releasing)
            {
                aligned = true;
                lastPivot = pl.parent; savedLocal = pl.localRotation;
                // (couche, le « haut » du joueur pointe vers sa tete : c'est lui qui donne le sens du corps -- PlayerSync envoie le cap des
                // pieds d'apres lui ; on tourne le joueur autour de la verticale pour que ses pieds visent le bas du lit)
                Vector3 up = pl.up; Vector3 feetNow = new Vector3(-up.x, 0f, -up.z);
                if (feetNow.sqrMagnitude > 0.05f)
                {
                    float a = Mathf.Atan2(feetNow.x, feetNow.z) * Mathf.Rad2Deg, b = Mathf.Atan2(feetDir.x, feetDir.z) * Mathf.Rad2Deg;
                    pl.rotation = Quaternion.Euler(0f, Mathf.DeltaAngle(a, b), 0f) * pl.rotation;
                }
                Log.Info("lit des parents : couche dans l'axe du lit (cap " + pl.eulerAngles.y.ToString("F0") + ")");
            }
            if (!here && !releasing && pl.parent != lastPivot) aligned = false;
        }

        // Plancher sous un point (le plus haut des solides non declencheurs, hors joueur) ; sinon la hauteur donnee.
        static float Floor(Vector3 p, float def)
        {
            float best = float.NegativeInfinity;
            foreach (RaycastHit h in Physics.RaycastAll(new Vector3(p.x, p.y + 1.2f, p.z), Vector3.down, 4f))
                if (h.collider != null && !h.collider.isTrigger && Game.RootName(h.collider.transform) != "PLAYER" && h.point.y > best && h.point.y <= p.y + 0.3f) best = h.point.y;
            return best > float.NegativeInfinity ? best : def;
        }

        static void Make()
        {
            GameObject tpl = Game.FindAny("YARD/Building/BEDROOM1/LOD_bedroom1/Sleep 1");
            GameObject bed = Game.FindAny("YARD/Building/BEDROOM2/bed_base/bed_soft");
            if (tpl == null || bed == null) { Log.Warn("lit des parents : " + (tpl == null ? "declencheur de sommeil du lit du joueur" : "lit des parents (bed_soft)") + " introuvable"); return; }
            Transform room = bed.transform.parent != null ? bed.transform.parent.parent : bed.transform.parent;
            if (room == null) room = bed.transform.parent;
            if (room.Find("Sleep MWCoop 1") != null) return;
            Bounds wb;
            Collider col = bed.GetComponent<Collider>(); Renderer rd = bed.GetComponent<Renderer>();
            if (col != null) wb = col.bounds; else if (rd != null) wb = rd.bounds; else wb = new Bounds(bed.transform.position, new Vector3(1.6f, 0.5f, 2f));
            // Axe long du lit (monde), des pieds vers la tete : la tete du cote de la table de nuit (bedtabl), s'il y en a une.
            // Axe long du lit : celui de ses bornes monde (2,1 m selon x, 1,8 selon z -- verifie en capture de dessus, 22/10 ; l'axe du
            // maillage dans son repere disait l'inverse). La tete de lit est a l'oppose de la porte de la chambre (DoorBedroom2) ; les
            // pieds vers la porte ; le corps parallele aux bords (montage de JD).
            Vector3 ax = wb.size.x >= wb.size.z ? Vector3.right : Vector3.forward;
            Transform door = room.Find("DoorBedroom2");
            if (door != null && Vector3.Dot(door.position - wb.center, ax) > 0f) ax = -ax;
            feetDir = -ax;
            Vector3 across = Vector3.Cross(Vector3.up, ax);
            float half = Config.GetFloat("Test", "LitParentsEcart", 0.30f);
            int made = 0;
            for (int side = 0; side < 2; side++)
                if (MakeOne(tpl, room, wb, ax, across * (side == 0 ? -half : half), side + 1)) made++;
            string wk = ""; foreach (Vector3 w in wakes) wk += " " + w.ToString("F2");
            Log.Info("lit des parents : " + made + " place(s) de sommeil sur " + Recon.Path(bed.transform) + " (lit " + wb.size.ToString("F1") + ", bas " + wb.min.y.ToString("F2") + ", axe " + ax.ToString("F0") + ", lever en" + wk + ")");
        }

        static bool MakeOne(GameObject tpl, Transform room, Bounds wb, Vector3 ax, Vector3 shift, int n)
        {
            var sl = (GameObject)Object.Instantiate(tpl);
            sl.name = "Sleep MWCoop " + n;
            sl.transform.SetParent(room, true);
            sl.SetActive(true);
            PlayMakerFSM fsm = null;
            foreach (PlayMakerFSM f in sl.GetComponentsInChildren<PlayMakerFSM>(true)) if (f.FsmName == "Activate") fsm = f;
            Transform pivot = sl.transform.Find("AnimPivotSleep");
            if (pivot != null)
            {
                pivots.Add(pivot);
                // Ou se relever : au pied du lit, de ce cote, sur le plancher (Floor ; sinon le bas du lit).
                float ext = Mathf.Abs(Vector3.Dot(wb.extents, ax));
                Vector3 foot = new Vector3(wb.center.x, wb.max.y, wb.center.z) + shift - ax * (ext + 0.45f);
                wakes.Add(new Vector3(foot.x, Floor(foot, wb.min.y), foot.z));
            }
            Transform head = pivot != null ? pivot.Find("Sphere") : null;
            foreach (Renderer r in sl.GetComponentsInChildren<Renderer>(true)) r.enabled = false;   // (reperes du corps : invisibles)
            // (k.x : la tete -- le pivot final, ou le jeu pose la camera du dormeur -- a cette distance du milieu vers la tete de lit)
            Vector3 k = ParseV(Config.Get("Test", "LitParents", ""), new Vector3(-0.35f, -0.37f, -0.50f));
            Vector3 across = Vector3.Cross(Vector3.up, ax);
            // L'animation du jeu (sleep_bed_in, sur AnimPivotSleep) deplace le pivot -- et le joueur, qui y est accroche -- de l'origine
            // du declencheur jusqu'au lit : c'est la pose FINALE du pivot qu'on aligne sur le lit (le modele « Sleep 1 » est a 1,4 m
            // de son lit pour cette raison). Pose finale echantillonnee sur la copie, puis remise au debut.
            Animation an = pivot != null ? pivot.GetComponent<Animation>() : null;
            AnimationClip clipIn = an != null ? an.GetClip("sleep_bed_in") : null;
            if (clipIn != null) clipIn.SampleAnimation(pivot.gameObject, clipIn.length);
            if (pivot != null && head != null)
            {
                Vector3 h = head.position - pivot.position; h.y = 0f;
                if (h.sqrMagnitude > 1e-4f) sl.transform.rotation = Quaternion.FromToRotation(h.normalized, ax) * sl.transform.rotation;
                Vector3 want = new Vector3(wb.center.x, wb.max.y, wb.center.z) + shift + ax * k.x + Vector3.up * k.y + across * k.z;
                sl.transform.position += want - pivot.position;
            }
            else sl.transform.position = new Vector3(wb.center.x, wb.max.y, wb.center.z) + shift;
            if (clipIn != null) clipIn.SampleAnimation(pivot.gameObject, 0f);
            if (fsm != null)
            {
                // Le declencheur (sphere visee par le joueur) au milieu du lit, un peu au-dessus du matelas.
                Transform trig = fsm.transform;
                trig.position = new Vector3(wb.center.x, wb.max.y + 0.25f, wb.center.z) + shift;
                var sc = trig.GetComponent<SphereCollider>();
                if (sc != null) sc.radius = 0.45f / Mathf.Max(0.01f, trig.lossyScale.x);
                if (!fsm.enabled) fsm.enabled = true;
            }
            return fsm != null;
        }

        static Vector3 ParseV(string s, Vector3 def)
        {
            string[] c = (s ?? "").Split(',');
            if (c.Length != 3) return def;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float x, y, z;
            if (float.TryParse(c[0], System.Globalization.NumberStyles.Float, ci, out x) && float.TryParse(c[1], System.Globalization.NumberStyles.Float, ci, out y) && float.TryParse(c[2], System.Globalization.NumberStyles.Float, ci, out z)) return new Vector3(x, y, z);
            return def;
        }
    }
}
