using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Tuyau de la GIFU (fosses septiques). "TAKE HOSE" (GIFU/ActivateHose 'Use', rejoue chez tous par Jobs) active
    // GIFU/HOSE : un raccord fixe au camion et une chaine de segments Hose(xxxxx) ; le joueur tient le DERNIER segment
    // et le plonge dans la fosse. Ce bout n'etait suivi nulle part : chez les autres il pendait au bout de la copie du
    // camion (a-coups du reseau) et la chaine s'envolait (retour de gamereggo, 11/10 : « le tuyau bugue et vole partout »).
    // Celui qui l'a eu en main en dernier envoie la pose du bout (10 fois/s tenu, 2 fois/s pose). Ailleurs TOUTE la chaine
    // est cinematique et sans collision, posee en courbe du raccord du camion au bout recu. (0.82.2 : seul le bout etait
    // cinematique, la chaine physique tiree entre lui et la copie du camion explosait en NaN au bout de 30 s -- jeu de
    // l'hote plante, « Infinity or nan ... for collider » ; et ce tuyau-la entrait et sortait de la fosse chez l'hote,
    // renvoye a l'invite qui ne pompait plus : OpNoob, 0.82.3.) Rendu physique au rangement, ou 3 s sans message.
    public static class SewageHose
    {
        static GameObject root;
        static Rigidbody end;
        static Transform coupler;
        static Rigidbody[] segs;
        static Collider[] cols;
        static bool following;
        static float chainLen;
        static bool mine, wasOut;
        static int remoteBy = -1;
        static float nextSend, lastRemote, nextLook;
        static Vector3 rp; static Quaternion rq;
        static int logs;
        static bool leading, ignoring, selfMode; static int oldRule = -1;
        static Collider[] gifuCols;
        static void IgnoreCopy(Transform gt, bool on)
        {
            if (on == ignoring || cols == null || gt == null) return;
            ignoring = on;
            if (gifuCols == null) gifuCols = gt.GetComponentsInChildren<Collider>(true);
            int n = 0;
            foreach (Collider a in cols)
            {
                if (a == null || !a.enabled) continue;
                foreach (Collider b in gifuCols)
                    if (b != null && b.enabled && !b.isTrigger && !b.transform.IsChildOf(root.transform)) { Physics.IgnoreCollision(a, b, on); n++; }
            }
            if (logs++ < 10) Log.Info("tuyau de la GIFU : chocs avec la copie du camion " + (on ? "coupes" : "rendus") + " (" + n + " paires)");
        }

        static Transform Gifu() { Rigidbody b = VehicleSync.Body("GIFU(750/450psi)"); return b != null ? b.transform : null; }

        public static void OnLevelLoaded() { leading = false; ignoring = false; gifuCols = null; root = null; end = null; coupler = null; segs = null; cols = null; following = false; mine = false; wasOut = false; remoteBy = -1; nextLook = 0; npcDone.Clear(); nextNpcLook = 0; }

        static bool Find()
        {
            if (end != null && root != null) return true;
            float now = Time.realtimeSinceStartup;
            if (now < nextLook) return false;
            nextLook = now + 5f;
            Transform gifu = Gifu();
            Transform h = gifu != null ? gifu.Find("HOSE") : null;
            root = h != null ? h.gameObject : null;
            if (root == null) return false;
            Transform c = root.transform.Find("hose coupler(xxxxx)");
            if (c == null) return false;
            Transform t = c;
            var l = new System.Collections.Generic.List<Rigidbody>();
            for (int i = 0; i < 200; i++) { Transform n = t.Find("Hose(xxxxx)"); if (n == null) break; t = n; Rigidbody rb = n.GetComponent<Rigidbody>(); if (rb != null) l.Add(rb); }
            end = t != c ? t.GetComponent<Rigidbody>() : null;
            coupler = c; segs = l.ToArray(); cols = root.GetComponentsInChildren<Collider>(true);
            couplerRb = c.GetComponent<Rigidbody>(); couplerParent = c.parent; couplerPos = c.localPosition; couplerRot = c.localRotation;
            if (end != null) Log.Info("tuyau de la GIFU : bout " + t.name + " (" + Recon.Path(t).Split('/').Length + " niveaux)");
            return end != null;
        }

        // Proprio de la fosse (JOBS/HouseShitN/LOD/ShitNPC 'Availability') : le jeu ne le montre, a l'activation de la maison
        // (a moins de 150 m), que si la fosse est pleine ET la commande passee. En coop, le joueur qui revient pendant que
        // l'autre pompe ne le voyait plus (gamereggo, 11/10 : « l'hote ne voit plus le proprio ») -- et n'aurait pas ete
        // paye. Ici : la commande passee suffit (seuil du niveau mis a -1).
        static float nextNpcLook;
        static readonly System.Collections.Generic.HashSet<PlayMakerFSM> npcDone = new System.Collections.Generic.HashSet<PlayMakerFSM>();
        static void PatchNpcs(float now)
        {
            if (now < nextNpcLook) return;
            nextNpcLook = now + 3f;
            for (int i = 1; i <= 6; i++)
            {
                GameObject g = Game.FindAny("JOBS/HouseShit" + i + "/LOD/ShitNPC");
                PlayMakerFSM f = g != null ? Game.FsmOn(g, "Availability") : null;
                if (f == null || npcDone.Contains(f) || f.Fsm == null || !f.Fsm.Initialized) continue;
                HutongGames.PlayMaker.FsmState st = f.Fsm.GetState("Check level");
                if (st == null || st.Actions == null) continue;
                foreach (HutongGames.PlayMaker.FsmStateAction a in st.Actions)
                {
                    var fc = a as HutongGames.PlayMaker.Actions.FloatCompare;
                    if (fc != null && fc.float2 != null && !fc.float2.UseVariable && fc.float2.Value > 4f) { fc.float2.Value = -1f; npcDone.Add(f); Log.Info("fosse : proprio de HouseShit" + i + " visible tant que la commande est passee"); }
                }
            }
        }

        public static void Update()
        {
            if (Session.Active && PlayerSync.InGame) PatchNpcs(Time.realtimeSinceStartup);
            if (!Session.Active || !PlayerSync.InGame || !Find()) return;
            float now = Time.realtimeSinceStartup;
            bool isOut = root.activeInHierarchy;
            if (!isOut)
            {
                if (wasOut) { Release(); IgnoreCopy(Gifu(), false); mine = false; remoteBy = -1; wasOut = false; leading = false; }
                return;
            }
            wasOut = true;
            bool held = FireTools.Held() == end.gameObject || testHeld;
            if (held && !mine) { mine = true; if (remoteBy >= 0) { remoteBy = -1; if (!testHeld) Release(); } }
            // Personne ne l'a encore eu en main (sorti, pas tenu) : celui qui mene la GIFU fait foi et envoie la pose du bout. Avant,
            // personne n'envoyait : chez les autres la chaine restait physique, accrochee au raccord de la COPIE cinematique du camion
            // (moteur tournant, recalee a chaque message), percutee a 15-80 km/h et projetee dans tous les sens (JD et GG, MSC,
            // 27/10 : « il partait dans tous les sens comme un serpent »). Chez eux, elle suit maintenant cette pose, sans physique.
            Transform gt = Gifu();
            if (oldRule < 0) oldRule = Config.GetInt("Test", "TuyauAncien", 0);   // (essais : regle d'avant le 27/10)
            bool lead = oldRule == 0 && !mine && remoteBy < 0 && gt != null && VehicleSync.SimAuthority(gt);
            if (lead && !leading) { leading = true; if (logs++ < 10) Log.Info("tuyau de la GIFU : sorti, pas encore en main : pose du bout envoyee d'ici (camion mene ici)"); }
            if (!lead) leading = false;
            // (chaine physique ici sur une copie du camion -- tenue ici : ses maillons ne heurtent plus les collisionneurs de la copie,
            // deplacee par les messages et pas par la physique)
            if (oldRule == 0) IgnoreCopy(gt, (!following || selfMode) && gt != null && !VehicleSync.SimAuthority(gt));
            if ((mine || lead) && Session.RemoteCount > 0 && now >= nextSend)
            {
                nextSend = now + (held ? 0.1f : 0.5f);
                Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@bout").Vec(end.position).Quat(end.rotation).Bool(held), false);
            }
            if (remoteBy >= 0)
            {
                if (now - lastRemote > 3f) { Release(); remoteBy = -1; return; }
                Follow();
            }
            else if (mine && oldRule == 0 && gt != null && !VehicleSync.SimAuthority(gt)) Follow(true, held);
            else if (selfMode) Release();
        }

        // Chaine posee sans physique : du raccord (sur le camion) au bout recu, en courbe (le mou pend).
        // self : chaine tenue ICI alors que le camion est une copie (deplacee par les messages) : la chaine physique, soudee au
        // ShitTank cinematique de la copie, explosait chez celui qui tenait le bout (essai MSC 27/10 : maillons a 15 000 m/s, a 800 km ;
        // GG chez JD : « il partait dans tous les sens comme un serpent ») et ses poses folles partaient chez les autres. Posee comme
        // chez les autres, du raccord au bout que la main tient (le bout reste au jeu).
        static bool endKin, selfHeld, couplerKin;
        // Raccord (corps soude au ShitTank) : sur une copie du camion, sa pose physique restait en arriere de la voiture deplacee
        // (Unity 5.0 : corps enfant en retard -- [MWC] 27/10, raccord a ~20 m chez l'invite, poses du bout fausses et refusees chez
        // l'hote au-dela de 30 m). Pendant le suivi : cinematique, a sa pose de repos sous HOSE.
        static Rigidbody couplerRb; static Transform couplerParent; static Vector3 couplerPos; static Quaternion couplerRot;
        static void Follow(bool self = false, bool held = false)
        {
            if (segs == null || segs.Length == 0 || coupler == null) return;
            if (following && self != selfMode) Release();
            if (!following)
            {
                following = true; selfMode = self; endKin = end.isKinematic; selfHeld = held;
                if (couplerRb != null) { couplerKin = couplerRb.isKinematic; couplerRb.isKinematic = true; }
                chainLen = 0f;
                Vector3 prev = coupler.position;
                foreach (Rigidbody r in segs) { chainLen += (r.position - prev).magnitude; prev = r.position; }
                if (!(chainLen > 1f && chainLen < 50f)) chainLen = segs.Length * 0.25f;
                foreach (Rigidbody r in segs) { if (!r.isKinematic) { r.velocity = Vector3.zero; r.angularVelocity = Vector3.zero; } r.isKinematic = true; }   // (le bout aussi : tenu, il suit la main -- son parent -- ; lache, il reste ou il est pose)
                foreach (Collider c in cols) if (c != null && !(self && c.attachedRigidbody == end)) c.enabled = false;
            }
            Vector3 ep0 = end.transform.position; Quaternion eq0 = end.transform.rotation;   // (le raccord est la racine de la chaine : le recaler deplace le bout)
            if (couplerRb != null && coupler.parent == couplerParent) { coupler.localPosition = couplerPos; coupler.localRotation = couplerRot; }
            if (self) { end.transform.position = ep0; end.transform.rotation = eq0; }
            if (self && !end.isKinematic) { end.isKinematic = true; end.velocity = Vector3.zero; }   // (la main ou le jeu l'ont rendu physique : la chaine posee ne le tire plus)
            if (self && selfHeld && !held) Ground();   // (lache ici : pose au sol)
            if (self) selfHeld = held;
            Vector3 a = coupler.position;
            Vector3 b = self ? end.transform.position : Vector3.Lerp(segs[segs.Length - 1].position, rp, 0.5f);
            float d = (b - a).magnitude, sag = Mathf.Max(0f, chainLen - d) * 0.5f;
            // (le mou ne descend pas sous le plus bas des deux bouts : tuyau pose par terre, pas sous le sol -- essai MSC 27/10, chaine a
            // 4,6 m du raccord chez l'invite pour 1,7 m chez celui qui la menait)
            sag = Mathf.Min(sag, Mathf.Max(0f, (a.y + b.y) * 0.5f - Mathf.Min(a.y, b.y) + 0.05f));
            int n = segs.Length;
            // (le bout est l'enfant du maillon d'avant tant que la main ne l'a pas repris : sa pose gardee et remise apres la passe)
            Vector3 ep = end.transform.position; Quaternion eq = end.transform.rotation;
            for (int i = 0; i < (self ? n - 1 : n); i++)
            {
                float k = (i + 1f) / n;
                Vector3 p = Vector3.Lerp(a, b, k) + Vector3.down * (sag * 4f * k * (1f - k));
                float k2 = Mathf.Min(1f, (i + 2f) / n);
                Vector3 q = Vector3.Lerp(a, b, k2) + Vector3.down * (sag * 4f * k2 * (1f - k2));
                Transform t = segs[i].transform;
                t.position = p;
                if (i == n - 1) t.rotation = Quaternion.Slerp(t.rotation, rq, 0.5f);
                else if ((q - p).sqrMagnitude > 1e-6f) t.rotation = Quaternion.LookRotation(q - p, Vector3.up);
            }
            if (self) { end.transform.position = ep; end.transform.rotation = eq; }
        }

        // Bout lache ici sur une copie (chaine posee) : sur le premier sol sous lui, pas en l'air.
        static void Ground()
        {
            Vector3 from = end.transform.position + Vector3.up * 0.5f;
            float best = float.MaxValue; Vector3 at = Vector3.zero;
            foreach (RaycastHit h in Physics.RaycastAll(from, Vector3.down, 30f, ~0))
            {
                if (h.collider == null || h.collider.isTrigger || h.collider.transform.IsChildOf(root.transform) || Game.UnderPlayer(h.collider.transform)) continue;
                if (h.distance < best) { best = h.distance; at = h.point; }
            }
            if (best < float.MaxValue) end.transform.position = at + Vector3.up * 0.06f;
        }

        static void Release()
        {
            if (!following) return;
            following = false; ignoring = false;
            bool wasSelf = selfMode; selfMode = false;
            if (couplerRb != null) couplerRb.isKinematic = couplerKin;   // (colliders rallumes : IgnoreCollision a refaire)
            if (segs != null) foreach (Rigidbody r in segs) if (r != null) { if (wasSelf && r == end) { r.isKinematic = endKin; continue; } r.isKinematic = false; r.velocity = Vector3.zero; r.angularVelocity = Vector3.zero; }
            if (cols != null) foreach (Collider c in cols) if (c != null) c.enabled = true;
        }

        // [Test] Autotest=fossepaie (2 joueurs, places loin de HouseShit3 par TestPos) : 40 s apres l'arrivee de l'autre,
        // l'hote passe la commande de HouseShit3 (niveau 4,95, ORDERTAKEN -> Full) ; 5 s apres l'invite va devant la maison ;
        // 15 s apres l'hote (autorite des fosses, GIFU garee) la vide (0,3 -> Pay) ; l'invite note a +25 s si le proprio est
        // la et tend l'argent (PayMoney allume), l'hote reste loin (maison hors de portee chez lui).
        static int tStep; static float tAt;
        static string TestWell { get { return Game.Msc ? "JOBS/HouseShit3/WasteWell_2000litre/Shit/Level3/ShitLevelTrigger" : "JOBS/HouseShit3/WasteWell_2000litre/Shit/Level/ShitLevelTrigger"; } }
        static string TestMan { get { return Game.Msc ? "JOBS/HouseShit3/LOD/ShitNPC/ShitMan3" : "JOBS/HouseShit3/LOD/ShitNPC/Man"; } }   // (MSC : Level{N}, ShitMan{N})
        // [Test] Autotest=tuyau (2 joueurs) : 40 s apres l'arrivee de l'autre, l'invite sort le tuyau (ActivateHose -> Remove
        // part, rejoue chez l'hote) puis promene le bout 10 s (comme tenu : il l'envoie) ; chacun note la pose du bout toutes
        // les 2 s ; a +16 s l'invite le range (Put hose back).
        static bool testHeld;
        static void TestHose(float now)
        {
            // ([Test] TuyauVolant=1 : l'hote au volant de la GIFU des 30 s, moteur lance -- rapport de JD 27/10 ; TuyauTenu=0 : l'invite sort
            // le tuyau sans le prendre en main ; releve : vitesse max des maillons et ecart max au raccord)
            if (Session.IsHost && Config.GetInt("Test", "TuyauVolant", 0) != 0 && Gifu() != null)
            {
                if (tvStep == 0 && Time.timeSinceLevelLoad > 30f) { tvStep = 1; Log.Info("autotest : tuyau, " + VehicleSync.TestEnter("GIFU(750/450psi)", false)); }
                if (tvStep == 1 && Time.timeSinceLevelLoad > 33f || tvStep == 2 && Time.timeSinceLevelLoad > 35f) { tvStep++; Log.Info("autotest : tuyau, au volant : " + VehicleSync.TestEnter("GIFU(750/450psi)", true)); }
                if (tvStep == 3 && Time.timeSinceLevelLoad > 37f) { tvStep = 4; Log.Info("autotest : tuyau, " + VehicleSync.TestStart("GIFU(750/450psi)")); }
            }
            // (TuyauVolant=1 : l'hote roule 6 s avec la GIFU, l'invite la voit bouger -- copie deplacee, comme JD avant que GG sorte le tuyau)
            if (Session.IsHost && Config.GetInt("Test", "TuyauVolant", 0) != 0 && tvStep == 4 && Jobs.OtherInGame(20f)) { tvStep = 5; tvAt = now; Log.Info("autotest : tuyau, la GIFU roule 6 s"); }
            if (tvStep == 5)
            {
                Rigidbody gb = VehicleSync.Body("GIFU(750/450psi)");
                if (gb != null && now - tvAt < 6f) { Vector3 f = gb.transform.forward; f.y = 0f; gb.velocity = f.normalized * 4f + Vector3.up * Mathf.Min(gb.velocity.y, 0f); }
                if (now - tvAt >= 6f) { tvStep = 6; if (gb != null) gb.velocity = Vector3.zero; Log.Info("autotest : tuyau, la GIFU s'arrete en " + (gb != null ? gb.position.ToString("F1") : "?")); }
            }
            if (tStep == 0 && Jobs.OtherInGame(40f)) { tStep = 1; tAt = now; }
            if (tStep == 0) return;
            Transform gt = Gifu(), at = gt != null ? gt.Find("ActivateHose") : null;
            GameObject ah = at != null ? at.gameObject : null;
            PlayMakerFSM use = ah != null ? Game.FsmOn(ah, "Use") : null;
            // (sortie et rangement poses des deux cotes : en vrai c'est le clic, rejoue par Jobs)
            if (Session.IsHost && use != null)
            {
                if (tStep == 1) { tStep = 2; Game.SetState(use, "Remove part"); }
                if (tStep == 2 && now - tAt > 50f) { tStep = 3; Game.SetState(use, "Put hose back"); }
            }
            if (!Session.IsHost)
            {
                if (tStep == 1 && use != null) { tStep = 2; Game.SetState(use, "Remove part"); Log.Info("autotest : tuyau, sorti -> " + use.ActiveStateName); }
                if (tStep == 2 && now - tAt > 2f && Find() && Config.GetInt("Test", "TuyauTenu", 1) != 0) {
                    // (comme la vraie prise en main : chaine rendue physique d'abord, puis le bout pris par une « main » -- son parent, comme
                    // ItemPivot -- deplacee ici ; l'ancien MovePosition sur un bout reste enfant de la chaine ne ressemblait pas au jeu)
                    tStep = 3; Release(); remoteBy = -1; testHeld = true; end.isKinematic = true;
                    testHand = new GameObject("MWCoopTestHand").transform; testHand.position = end.transform.position; endParent = end.transform.parent; end.transform.parent = testHand;
                }
                if (tStep == 3 && end != null)
                {
                    float k = now - tAt - 2f;
                    Vector3 basePos = ah.transform.position + ah.transform.right * 3f;
                    if (testHand != null) testHand.position = basePos + new Vector3(Mathf.Sin(k) * 2f, 0.5f, Mathf.Cos(k) * 2f);
                    if (k > 10f) { tStep = 4; testHeld = false; end.isKinematic = false; if (testHand != null) { end.transform.parent = endParent; Object.Destroy(testHand.gameObject); testHand = null; } }
                }
                if (tStep == 4 && now - tAt > 50f && use != null) { tStep = 5; Game.SetState(use, "Put hose back"); Log.Info("autotest : tuyau, range -> " + use.ActiveStateName); }
            }
            if (Find() && now >= nextTestLog && now - tAt < 56f)
            {
                nextTestLog = now + 4f;
                float vmax = 0f, dmax = 0f;
                if (segs != null && coupler != null) foreach (Rigidbody r in segs) if (r != null) { vmax = Mathf.Max(vmax, r.velocity.magnitude); dmax = Mathf.Max(dmax, (r.position - coupler.position).magnitude); }
                Transform stt = Gifu() != null ? Gifu().Find("ShitTank") : null; Rigidbody stb = stt != null ? stt.GetComponent<Rigidbody>() : null;
                if (stb != null) Log.Info("autotest : tuyau, ShitTank : corps a " + (stb.position - stt.position).magnitude.ToString("F2") + " m de son objet" + (stb.isKinematic ? " (cin)" : " (libre)") + ", raccord a " + (coupler != null ? (coupler.position - stt.position).magnitude.ToString("F2") : "?") + " m");
                Log.Info("autotest : tuyau, maillons : vitesse max " + vmax.ToString("F1") + " m/s, ecart max au raccord " + dmax.ToString("F1") + " m" + (following ? " (suivi)" : "") + (ignoring ? " (chocs copie coupes)" : ""));
                Log.Info("autotest : tuyau, " + (root.activeInHierarchy ? "sorti" : "range") + ", bout en " + end.position.ToString("F2") + (end.isKinematic ? " (cinematique)" : "") + (mine ? " a nous" : remoteBy >= 0 ? " suivi sur #" + remoteBy : "") + (following ? ", chaine posee" : "") + (cols != null && cols.Length > 0 && cols[0] != null && !cols[0].enabled ? ", sans collision" : ""));
            }
        }
        static float nextTestLog, tvAt; static int tvStep;
        static Transform testHand, endParent;

        public static void Test(string mode, float t)
        {
            if (mode == "tuyau") { TestHose(Time.realtimeSinceStartup); return; }
            if (mode != "fossepaie") return;
            float now = Time.realtimeSinceStartup;
            if (tStep == 0 && Jobs.OtherInGame(40f)) { tStep = 1; tAt = now; }
            if (tStep == 0) return;
            GameObject wg = Game.FindAny(TestWell);
            PlayMakerFSM lv = wg != null ? Game.FsmOn(wg, "Level") : null;
            FsmFloat level = lv != null ? lv.FsmVariables.FindFsmFloat("ShitLevel") : null;
            if (Session.IsHost)
            {
                if (tStep == 1 && lv != null) { tStep = 2; level.Value = 4.95f; lv.SendEvent("ORDERTAKEN"); Log.Info("autotest : fossepaie, commande passee -> " + lv.ActiveStateName); }
                if (tStep == 2 && now - tAt > 15f && level != null) { tStep = 3; level.Value = 0.3f; Log.Info("autotest : fossepaie, fosse videe ici (" + lv.ActiveStateName + ")"); }
                if (tStep == 3 && now - tAt > 20f) { tStep = 4; Log.Info("autotest : fossepaie, hote : niveau " + (level != null ? level.Value.ToString("F2") : "?") + ", etat " + (lv != null ? lv.ActiveStateName : "?")); }
                return;
            }
            if (tStep == 1 && now - tAt > 5f)
            {
                tStep = 2;
                GameObject pl = Game.PlayerGo;
                var cc = pl != null ? pl.GetComponent<CharacterController>() : null;
                if (cc != null) cc.enabled = false;
                if (pl != null) pl.transform.position = new Vector3(-1350.5f, 4.2f, 1207.5f);
                if (cc != null) cc.enabled = true;
                Log.Info("autotest : fossepaie, invite devant la maison");
            }
            if ((tStep == 2 && now - tAt > 12f) || (tStep == 3 && now - tAt > 30f))
            {
                tStep++;
                GameObject man = Game.FindAny(TestMan);
                PlayMakerFSM an = man != null ? Game.FsmOn(man, "Anims") : null;
                Transform pay = man != null ? FindDeep(man.transform, "PayMoney") : null;
                Log.Info("autotest : fossepaie, invite : niveau " + (level != null ? level.Value.ToString("F2") : "?") + ", fosse " + (lv != null ? lv.ActiveStateName : "?")
                         + ", proprio " + (man != null && man.activeInHierarchy ? "LA" : "absent") + ", gestes " + (an != null ? an.ActiveStateName + (an.enabled ? "" : " (arrete)") : "?")
                         + ", argent tendu " + (pay != null && pay.gameObject.activeInHierarchy ? "OUI" : "non"));
            }
        }
        static Transform FindDeep(Transform t, string n)
        {
            if (t.name == n) return t;
            for (int i = 0; i < t.childCount; i++) { Transform r = FindDeep(t.GetChild(i), n); if (r != null) return r; }
            return null;
        }

        public static void OnRemote(int who, Vector3 p, Quaternion q, bool held)
        {
            if (!Find() || !root.activeInHierarchy) return;
            if (FireTools.Held() == end.gameObject) return;   // (on le tient ici : le notre fait foi)
            if (remoteBy != who && logs++ < 10) Log.Info("tuyau de la GIFU : bout suivi sur #" + who + (held ? " (en main)" : " (pose)"));
            if (float.IsNaN(p.x + p.y + p.z) || float.IsInfinity(p.x + p.y + p.z) || (p - (coupler != null ? coupler.position : p)).sqrMagnitude > 900f) return;   // (rien de fou : 30 m au plus du camion)
            mine = false; remoteBy = who; rp = p; rq = q; lastRemote = Time.realtimeSinceStartup;
        }
    }
}
