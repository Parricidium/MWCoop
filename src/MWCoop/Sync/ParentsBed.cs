using HutongGames.PlayMaker;
using UnityEngine;

namespace MWCoop
{
    // Dormir dans la chambre des parents (Maxim Pennis, 15/10 ; JD, 19/10 : oui ; My Summer Car aussi, suggestion 35, JD 06/11). Le grand lit de la maison des parents
    // (YARD/Building/BEDROOM2 : bed_base/bed_soft) n'a pas de declencheur de sommeil ; celui du lit du joueur (BEDROOM1/
    // LOD_bedroom1/Sleep 1 : automate 'Activate' sur SleepTrigger, pivot anime AnimPivotSleep) est copie dessus : le corps dans
    // la longueur du lit, la tete du cote de la table de nuit, a la hauteur du matelas. Copie faite chez chacun, 14 s apres le
    // chargement ; dormir y fait comme dans son lit (temps, sauvegarde : l'automate du jeu). Corps remonte de 80 cm vers
    // la tete de lit (le milieu pivot-tete de l'animation n'est pas le milieu du corps : il depassait au pied du lit, 22/10).
    // [Test] LitParents=dx,dy,dz : decalage du corps (repere du monde : axe du lit, haut, travers), par defaut 0 / 0,08 / 0.
    public static class ParentsBed
    {
        static float at = -1; static bool done, aligned, releasing;
        static readonly System.Collections.Generic.List<Transform> pivots = new System.Collections.Generic.List<Transform>();
        static readonly System.Collections.Generic.List<Vector3> wakes = new System.Collections.Generic.List<Vector3>();   // (par place : ou se relever, sur le plancher)
        static Vector3 feetDir;   // (sens des pieds dans le lit, monde)
        static Transform lastPivot; static Quaternion savedLocal; static Vector3 wakeAt;

        public static void OnLevelLoaded() { at = PlayerSync.InGame ? Time.realtimeSinceStartup + 14f : -1; done = false; aligned = false; releasing = false; pivots.Clear(); wakes.Clear(); }

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
            GameObject tpl = Game.FindAny(Game.Msc ? "YARD/Building/BEDROOM1/LOD_bedroom1/Sleep" : "YARD/Building/BEDROOM1/LOD_bedroom1/Sleep 1");
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
            Transform pivot = sl.transform.Find(Game.Msc ? "AnimPivot" : "AnimPivotSleep");
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
            Vector3 k = ParseV(Config.Get("Test", "LitParents", ""), new Vector3(Game.Msc ? -0.40f : -0.35f, -0.37f, 0f));   // (pas d'ecart de travers entre le pivot et le corps : -0,5 mettait la place 1 a 0,8 m du centre, a moitie hors du lit, MWC compris depuis le 22/10 ; vu sur photos, 06/11)
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

        // [Test] Autotest=litparents : a 25 s, le joueur a cote du lit se couche (l'automate du jeu, seul le clic n'est pas rejoue) a la place [Test] LitPlace (1 ou 2) ; position du joueur
        // et de la camera toutes les 2 s jusqu'au reveil, photos couche et debout (dumps	uyau-*lit*.png).
        static int tStep; static float tNext, tAt;
        static bool tEye; static float tWoke;
        static string Etat()
        {
            PlayMakerFSM c = Game.FindFsm("MAP/SUN/Pivot/SUN", "Color"); FsmVariables g = FsmVariables.GlobalVariables;
            System.Func<string, string> v = n => { FsmFloat x = g.FindFsmFloat(n); return n + " " + (x != null ? x.Value.ToString("F1") : "?"); };
            FsmInt day = g.FindFsmInt("GlobalDay");
            return "heure " + (c != null ? c.FsmVariables.GetFsmInt("Time").Value.ToString() : "?") + ", jour " + (day != null ? day.Value : -1) + ", " + v("PlayerFatigue") + ", " + v("PlayerHunger") + ", " + v("PlayerThirst");
        }
        public static void Test(float t)
        {
            // ([Test] LitVue=s : pas de sommeil ici, photos du lit toutes les 2 s pendant 24 s a partir de s : l'avatar couche de l'autre joueur)
            float vue = Config.GetFloat("Test", "LitVue", 0f);
            if (vue > 0f)
            {
                GameObject vb = Game.FindAny("YARD/Building/BEDROOM2/bed_base/bed_soft");
                if (vb != null && t > vue && t < vue + 24f && Time.realtimeSinceStartup >= tNext)
                {
                    tNext = Time.realtimeSinceStartup + 2f; int k = (int)(t - vue);
                    Vector3 c = vb.GetComponent<Collider>().bounds.center;
                    SewageHose.Shot("lit-vue" + k + "-haut", c + Vector3.up * 2.4f + Vector3.right * 0.1f, c);
                    SewageHose.Shot("lit-vue" + k + "-pied", c + feetDir * 2.4f + Vector3.up * 1.0f, c);
                }
                return;
            }
            if (t < Config.GetFloat("Test", "LitA", 25f)) return;
            if (tStep == 0 && Config.GetInt("Test", "LitSec", -1) >= 0 && System.DateTime.Now.Second != Config.GetInt("Test", "LitSec", -1)) return;   // (les deux joueurs a la meme seconde de l'horloge du PC)
            if (tStep == 0 && t > 25f)
            {
                tStep = 1;
                string nom = "Sleep MWCoop " + Config.GetInt("Test", "LitPlace", 1);
                GameObject room = Game.FindAny("YARD/Building/BEDROOM2");
                Transform sl = room != null ? room.transform.Find(nom) : null;
                PlayMakerFSM f = null;
                if (sl != null) foreach (PlayMakerFSM x in sl.GetComponentsInChildren<PlayMakerFSM>(true)) if (x.FsmName == "Activate") f = x;
                Log.Info("autotest : litparents, " + nom + " " + (sl != null ? "trouve en " + sl.position.ToString("F2") : "INTROUVABLE") + ", automate " + (f != null ? f.ActiveStateName : "absent") + ", places " + pivots.Count);
                if (Config.GetInt("Test", "LitMoi", 0) != 0) { GameObject mine = Game.FindAny("YARD/Building/BEDROOM1/LOD_bedroom1/Sleep/SleepTrigger"); f = mine != null ? Game.FsmOn(mine, "Activate") : null; Log.Info("autotest : litparents, temoin : lit du joueur " + (f != null)); }   // (temoin : le lit du jeu)
                if (f == null) return;
                GameObject pl = Game.PlayerGo; var cc = pl.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false; pl.transform.position = f.transform.position + feetDir * 1.6f + Vector3.up * 0.3f; if (cc != null) cc.enabled = true;
                FsmFloat fat = FsmVariables.GlobalVariables.FindFsmFloat("PlayerFatigue"); if (fat != null) fat.Value = 30f;
                Log.Info("autotest : litparents, avant " + Etat());
                Game.SetState(f, "Get positions");
                tAt = Time.realtimeSinceStartup;
            }
            if (tStep == 0) return;
            float now = Time.realtimeSinceStartup;
            if (now >= tNext && now - tAt < 120f)
            {
                tNext = now + 2f;
                Transform pl = Game.PlayerT; Camera cam = Camera.main;
                bool sl = Game.GlobalBool("PlayerSleeps");
                Log.Info("autotest : litparents +" + (now - tAt).ToString("F0") + " s : dort " + sl + ", joueur " + (pl != null ? pl.position.ToString("F2") + " haut " + pl.up.ToString("F2") + " parent " + (pl.parent != null ? pl.parent.name : "aucun") : "?") + ", camera " + (cam != null ? cam.transform.position.ToString("F2") : "?"));
                GameObject bed = Game.FindAny("YARD/Building/BEDROOM2/bed_base/bed_soft");
                if (bed != null && pl != null)
                {
                    Vector3 c = bed.transform.position;
                    if (!tEye && sl && cam != null) { tEye = true; Log.Info("autotest : litparents, couche " + Etat()); SewageHose.Shot("lit-yeux", cam.transform.position, cam.transform.position + cam.transform.forward); Log.Info("autotest : litparents, camera " + cam.transform.position.ToString("F2") + " vise " + cam.transform.forward.ToString("F2") + ", mouvement " + (Game.PlayerT != null ? Game.PlayerT.localEulerAngles.ToString("F0") : "?")); }
                    if (tStep == 1 && sl) { tStep = 2; SewageHose.Shot("lit-couche-haut", c + Vector3.up * 2.2f + Vector3.right * 0.2f, pl.position); SewageHose.Shot("lit-couche-cote", c + feetDir * 2.6f + Vector3.up * 1.1f, pl.position); }
                    if (tStep == 3 && Config.GetInt("Test", "LitSauve", 0) != 0 && now - tWoke > 4f && Net.Session.IsHost) { tStep = 4; Log.Info("autotest : litparents, sauvegarde apres le reveil, " + Etat()); Game.SaveInPlace(); }
                    if (tStep == 2 && !sl && pl.parent == null) { tWoke = now; tStep = 3; SewageHose.Shot("lit-debout", pl.position + feetDir * 2f + Vector3.up * 1f, pl.position); Log.Info("autotest : litparents, apres " + Etat()); Log.Info("autotest : litparents, reveil ; sol sous le joueur " + Floor(pl.position, float.NaN).ToString("F2") + ", attendu lever " + wakeAt.ToString("F2")); }
                }
            }
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
