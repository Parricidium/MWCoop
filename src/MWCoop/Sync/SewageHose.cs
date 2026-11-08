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

        public static void OnLevelLoaded() { logs = 0; leading = false; ignoring = false; gifuCols = null; root = null; end = null; coupler = null; segs = null; cols = null; following = false; mine = false; wasOut = false; remoteBy = -1; nextLook = 0; chainIn = null; chainOut = null; chainBy = -1; nextChain = 0f; hStep = 0; eLog = 0; sStep = 0; sPrev = null; nextShape = 0f; pStep = 0; simWas = false; shapeAt = 0f; shapeLogs = 0; sG0 = Vector3.zero; dampUntil = 0f; dragSaved = null; chainMoving = false; wellIgnored.Clear(); wellCols.Clear(); nextWellCols = 0f; npcDone.Clear(); nextNpcLook = 0; }

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
            // (longueur de la chaine au repos, mesuree une fois ici -- tuyau range, maillons a leur pose d'origine. Mesuree au debut du
            // suivi, sur une chaine deja etiree par la prise en main, elle doublait : 18,6 m pour 9,1 m chez l'invite qui tenait le bout,
            // maillons espaces de 0,93 m -- [MWC] 28/10)
            restLen = 0f; Vector3 rprev = c.position;
            foreach (Rigidbody rb in segs) { restLen += (rb.transform.position - rprev).magnitude; rprev = rb.transform.position; }
            if (!(restLen > 1f && restLen < 50f)) restLen = segs.Length * 0.38f;
            linkAxis = new Vector3[segs.Length];
            for (int i = 0; i < segs.Length; i++)
            {
                Vector3 ax = i + 1 < segs.Length ? segs[i + 1].transform.localPosition : (i > 0 ? linkAxis[i - 1] : Vector3.forward);
                linkAxis[i] = ax.sqrMagnitude > 1e-6f ? ax.normalized : Vector3.forward;
            }
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
                if (wasOut) { chainIn = null; Release(); IgnoreCopy(Gifu(), false); mine = false; remoteBy = -1; wasOut = false; leading = false; }
                return;
            }
            wasOut = true;
            // (dossier #55 : chaine posee sans physique ici -- la chaine physique, elle, se tire par un maillon comme dans le jeu)
            if (Bord && following) { GameObject ph = FireTools.Held(); if (ph != null && ph != end.gameObject && ph.transform.IsChildOf(root.transform)) GiveEnd(ph); }
            bool held = FireTools.Held() == end.gameObject || testHeld;
            if ((mine || held || leading) && Forme) chainIn = null;   // (la forme d'un autre, d'avant : pas pour la prochaine fois qu'on suivra)
            if (held && !mine) { mine = true; if (remoteBy >= 0) { remoteBy = -1; if (!testHeld) Release(); } Jobs.HoseTakeover(end.transform); }
            // Bout lache ici (camion en copie), puis le camion s'en va : la pose figee d'ici etirait la chaine chez tous (15,7 m quand le
            // camion avait roule 24 m, [MWC] 28/10). Au-dela de sa longueur, la main est rendue : celui qui mene la GIFU reprend la pose
            // (sa chaine physique suit le camion), les autres la suivent.
            Transform gtr = Gifu();
            if (mine && !held && selfMode && dropSet && gtr != null && !VehicleSync.SimAuthority(gtr) && coupler != null && restLen > 1f
                && (end.transform.position - coupler.position).magnitude > restLen * 1.05f)
            {
                // (en suivi, sans physique, vers la derniere pose, en attendant celles de l'autorite -- 3 s au plus : sa reprise apres le silence)
                Rigidbody gb = gtr.GetComponent<Rigidbody>();
                int auth = gb != null ? VehicleSync.Authority(gb) : 0;
                mine = false; Release();
                remoteBy = auth; rp = end.transform.position; rq = end.transform.rotation; lastRemote = Time.realtimeSinceStartup + 3f;
                if (logs++ < 10) Log.Info("tuyau de la GIFU : camion parti, bout lache trop loin (" + (end.transform.position - coupler.position).magnitude.ToString("F1") + " m) : pose rendue a celui qui mene la GIFU");
            }
            // Personne ne l'a encore eu en main (sorti, pas tenu) : celui qui mene la GIFU fait foi et envoie la pose du bout. Avant,
            // personne n'envoyait : chez les autres la chaine restait physique, accrochee au raccord de la COPIE cinematique du camion
            // (moteur tournant, recalee a chaque message), percutee a 15-80 km/h et projetee dans tous les sens (JD et GG, MSC,
            // 27/10 : « il partait dans tous les sens comme un serpent »). Chez eux, elle suit maintenant cette pose, sans physique.
            Transform gt = Gifu();
            if (oldRule < 0) oldRule = Config.GetInt("Test", "TuyauAncien", 0);   // (essais : regle d'avant le 27/10)
            bool lead = oldRule == 0 && !mine && remoteBy < 0 && gt != null && VehicleSync.SimAuthority(gt);
            if (lead && !leading) { leading = true; if (logs++ < 10) Log.Info("tuyau de la GIFU : sorti, pas encore en main : pose du bout envoyee d'ici (camion mene ici)"); Jobs.HoseTakeover(end.transform); }
            if (!lead) leading = false;
            // (chaine physique ici sur une copie du camion -- tenue ici : ses maillons ne heurtent plus les collisionneurs de la copie,
            // deplacee par les messages et pas par la physique)
            // (dossier #50, Markos 04/11 : bout lache ici, puis il monte lui-meme au volant -- la chaine posee sans physique etait rendue d'un coup a la
            // physique, a cote de la Satsuma : « heurte Hose a 282 km/h », pare-brise casses. Elle reste posee tant que le camion ne bouge pas de 30 cm.)
            bool simHere = gt != null && VehicleSync.SimAuthority(gt);
            if (selfMode && !simHere && coupler != null) stayAt = coupler.position;
            // (dossier #50, 06/11, vu sur photo : bout pose ici, l'autre descend du volant, le camion reprend sa physique ici et le raccord saute de plus de 30 cm : la chaine posee etait
            // rendue d'un coup a la physique, maillons au ras du sol, et traversait le terrain -- jusqu'a 7 m dessous, le joueur qui allait la chercher tombait sous la carte. 2 s de grace.)
            if (gt != null && simHere && !simWas && Forme) stayGrace = now + 2f;
            if (selfMode && simHere && now < stayGrace && coupler != null) stayAt = coupler.position;
            // (dossier #50 : « coince dans le sol quand quelqu'un monte, et le camion cale » : pas reproduit, releve au changement de mains et 2 s apres)
            if (gt != null && simHere != simWas) { simWas = simHere; shapeAt = now + 2f; ShapeLog(gt, "camion " + (simHere ? "ici" : "copie")); }
            else if (gt != null && shapeAt > 0f && now >= shapeAt) { shapeAt = 0f; ShapeLog(gt, "2 s apres"); }
            bool stay = Forme && selfMode && !held && simHere && coupler != null && (coupler.position - stayAt).sqrMagnitude < 0.09f;
            if (dampUntil > 0f && now >= dampUntil) Undamp();
            if (oldRule == 0) IgnoreCopy(gt, ((!following || selfMode) && gt != null && !simHere) || stay);
            if ((mine || lead) && Session.RemoteCount > 0 && now >= nextSend)
            {
                nextSend = now + (held ? 0.1f : chainMoving && Forme ? 0.2f : 0.5f);
                Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@bout").Vec(end.position).Quat(end.rotation).Bool(held), false);
                if (Chaine && gt != null) SendChain(gt, now, held);
            }
            // (le camion roule ici et celui qui a lache le bout n'y est pour rien : la chaine physique d'ici suit le camion, tout de suite. Avant : forme
            // calculee chez l'autre, puis reprise quand elle etait etiree au-dela de sa longueur -- sauts de 2 a 4 m, essai TuyauSiegeHote)
            if (remoteBy >= 0 && Forme && !remoteHeld && following && !selfMode && simHere)
            {
                Rigidbody tb = gt.GetComponent<Rigidbody>();
                if (tb != null && !tb.isKinematic && tb.velocity.sqrMagnitude > 0.25f)
                {
                    Release(); remoteBy = -1;
                    if (logs++ < 10) Log.Info("tuyau de la GIFU : le camion roule ici, la chaine le suit d'ici");
                    return;
                }
            }
            if (remoteBy >= 0)
            {
                if (now - lastRemote > 3f)
                {
                    // (camion en copie ici : la chaine reste posee sans physique sur la derniere pose recue -- rendue physique, accrochee a la
                    // copie, elle s'etirait puis explosait si personne n'envoyait plus rien ([MWC] 28/10 : 24 m apres la grace de 6 s))
                    if (gt != null && !VehicleSync.SimAuthority(gt)) { Follow(); return; }
                    Release(); remoteBy = -1; return;
                }
                Follow();
            }
            else if (mine && oldRule == 0 && gt != null && (!simHere || stay)) Follow(true, held);
            else if (selfMode) Release();
        }

        // Chaine posee sans physique : du raccord (sur le camion) au bout recu, en courbe (le mou pend).
        // self : chaine tenue ICI alors que le camion est une copie (deplacee par les messages) : la chaine physique, soudee au
        // ShitTank cinematique de la copie, explosait chez celui qui tenait le bout (essai MSC 27/10 : maillons a 15 000 m/s, a 800 km ;
        // GG chez JD : « il partait dans tous les sens comme un serpent ») et ses poses folles partaient chez les autres. Posee comme
        // chez les autres, du raccord au bout que la main tient (le bout reste au jeu).
        static bool endKin, selfHeld, couplerKin, dropSet, remoteHeld, endGrab; static float restLen; static Vector3 dropPos; static Quaternion dropRot;
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
                keepPts = null; keepSet = false;
                if (self && !held && Forme) { keepPts = new Vector3[segs.Length]; keepRot = new Quaternion[segs.Length]; for (int i = 0; i < segs.Length; i++) { keepPts[i] = segs[i].transform.position; keepRot[i] = segs[i].transform.rotation; } dropPos = end.transform.position; dropRot = end.transform.rotation; dropSet = true; }
                if (couplerRb != null) { couplerKin = couplerRb.isKinematic; couplerRb.isKinematic = true; }
                chainLen = restLen > 1f ? restLen : segs.Length * 0.38f;
                foreach (Rigidbody r in segs) { if (!r.isKinematic) { r.velocity = Vector3.zero; r.angularVelocity = Vector3.zero; } r.isKinematic = true; }   // (le bout aussi : tenu, il suit la main -- son parent -- ; lache, il reste ou il est pose)
                foreach (Collider c in cols) if (c != null && !(self && c.attachedRigidbody == end)) c.enabled = false;
                endGrab = false; rimOn = false; pitAt = 0f;   // (maillons du bord : revus au premier passage, RimLinks)
            }
            // Bout pose par l'autre (pas en main chez lui) : son collisionneur reste allume ici, cinematique, pour que la main du jeu puisse
            // le reprendre. Tout eteint, personne d'autre ne pouvait plus le saisir tant que celui qui mene la GIFU l'envoyait : le
            // client avait sorti le tuyau, le camion de l'hote menait le bout, tuyau intouchable chez lui, coince dans un mur du Futufon
            // jusqu'a ce qu'il prenne le volant (Донской, 01/11). [Test] TuyauBoutEteint=1 : comme avant.
            bool grab = !self && !remoteHeld && Config.GetInt("Test", "TuyauBoutEteint", 0) == 0;
            if (grab != endGrab)
            {
                endGrab = grab;
                foreach (Collider c in cols) if (c != null && c.GetComponentInParent<Rigidbody>() == end) c.enabled = grab || self;   // (attachedRigidbody : null pour un collisionneur eteint)
            }
            Vector3 ep0 = end.transform.position; Quaternion eq0 = end.transform.rotation;   // (le raccord est la racine de la chaine : le recaler deplace le bout)
            if (couplerRb != null && coupler.parent == couplerParent) { coupler.localPosition = couplerPos; coupler.localRotation = couplerRot; }
            // (tuyau sorti, le jeu met le raccord a la racine de la scene : cinematique ici, il restait sur place quand le camion
            // repartait -- a 12 m de la citerne chez l'hote, essai tuyau 31/10, Markos dossier #29 : « le tuyau passe sous le sol ».
            // Pose de repos gardee sous HOSE, qui suit le camion.)
            else if (couplerRb != null && couplerParent != null && Config.GetInt("Test", "TuyauEtire", 0) == 0) { coupler.position = couplerParent.TransformPoint(couplerPos); coupler.rotation = couplerParent.rotation * couplerRot; }
            if (self) { end.transform.position = ep0; end.transform.rotation = eq0; }
            if (self && held && !testHeld) HandFollow(a0: coupler.position); else handSet = false;
            if (testHeld) endKin = false;   // (la main du jeu ne rend pas le bout cinematique, celle de l'essai si : rendu libre comme en vrai)
            if (self && !end.isKinematic) { end.isKinematic = true; end.velocity = Vector3.zero; }   // (la main ou le jeu l'ont rendu physique : la chaine posee ne le tire plus)
            // (lache ici : pose au sol, puis tenu a cette pose -- redevenu l'enfant du maillon d'avant, il suivait la chaine que l'on
            // replace a chaque image et derivait : 30 m du raccord, sous le sol, essai MSC 28/10)
            if (self && !held)
            {
                if (selfHeld || !dropSet) { Ground(); dropPos = end.transform.position; dropRot = end.transform.rotation; dropSet = true; }
                end.transform.position = dropPos; end.transform.rotation = dropRot;
            }
            if (self) { if (held) dropSet = false; selfHeld = held; }
            Vector3 a = coupler.position;
            Vector3 b = self ? end.transform.position : Vector3.Lerp(segs[segs.Length - 1].position, rp, 0.5f);
            int n = segs.Length;
            Vector3[] pts = Layout(a, b, chainLen, n);
            // (forme de la source, dans le repere du camion : maillons tires vers elle, le bout reste sur sa pose a 10/s)
            Transform gc = self ? null : Gifu();
            // (regression de 032da8e, dossiers #41/#47, 03/11 : forme perimee au bout de 1,5 s alors qu'une chaine au repos n'est renvoyee que toutes
            // les 2 s -- retour a la forme calculee 0,5 s sur 2, saut de 3,6 m en plein repos, essai TuyauSiege ; et des qu'un joueur montait au volant,
            // la source se taisant, forme calculee jusqu'a la reprise. La derniere forme recue reste bonne, elle est dans le repere du camion ; sauf
            // si la chaine de la source est restee loin derriere le camion : du raccord a son premier maillon, plus rien de vrai. [Test] TuyauForme=0 : comme avant.)
            bool useChain = gc != null && Chaine && chainIn != null && chainIn.Length == n
                            && (Forme ? (gc.TransformPoint(chainIn[0]) - a).sqrMagnitude < 9f : chainBy == remoteBy && Time.realtimeSinceStartup - chainAt < 1.5f);
            if (useChain) for (int i = 0; i < n - 1; i++) pts[i + 1] = Vector3.Lerp(segs[i].transform.position, gc.TransformPoint(chainIn[i]), 0.35f);
            if (useChain) chainFrames++; else if (!self) layoutFrames++;
            // (bout lache ici, chaine physique jusque-la, et le camion devient une copie -- l'autre monte au volant : la chaine ne bouge pas. Avant, elle
            // passait a la forme calculee, chez celui-ci et chez les autres. Tant que le camion reste la.)
            // (dossier #50 : tuyau pris en main sur une copie du camion, de sa forme physique a la forme calculee d'un coup -- saut de 2,6 a 4 m ; par petits pas)
            if (Forme && !useChain && (self ? held : true)) for (int i = 0; i < n - 1; i++) pts[i + 1] = Vector3.Lerp(segs[i].transform.position, pts[i + 1], 0.25f);
            bool keepNow = false;
            if (self && !held && keepPts != null && keepPts.Length == n)
            {
                if (!keepSet) { keepSet = true; keepAt = a; }
                keepNow = (a - keepAt).sqrMagnitude < 0.09f;
                if (keepNow) for (int i = 0; i < n; i++) pts[i + 1] = keepPts[i];
                else keepPts = null;
            }
            else if (self) keepPts = null;
            // (le bout est l'enfant du maillon d'avant tant que la main ne l'a pas repris : sa pose gardee et remise apres la passe)
            Vector3 ep = end.transform.position; Quaternion eq = end.transform.rotation;
            for (int i = 0; i < (self ? n - 1 : n); i++)
            {
                Vector3 p = pts[i + 1], q = i + 2 <= n ? pts[i + 2] : pts[n] + (pts[n] - pts[n - 1]);
                Transform t = segs[i].transform;
                t.position = p;
                if (i == n - 1) t.rotation = Quaternion.Slerp(t.rotation, rq, 0.5f);
                else if (keepNow) t.rotation = keepRot[i];
                else if (useChain && chainRot != null && chainRot.Length == n) t.rotation = Quaternion.Slerp(t.rotation, gc.rotation * chainRot[i], 0.35f);
                else if ((q - p).sqrMagnitude > 1e-6f) Aim(t, i, q - p);
            }
            if (self) { end.transform.position = ep; end.transform.rotation = eq; }
            IgnoreWells(!self && Jobs.HoseReprise);
            RimLinks(self ? !held : !remoteHeld);
        }

        // Dossier #55 (Markos, 08/11 : « le tuyau repose de travers au fond, on n'arrive pas a le saisir »). La main du jeu ne saisit qu'a 1 m
        // de la camera (PickUp : MousePick rayDistance 1, calques 19/15/9). Bout pose dans une fosse, chez qui le camion n'est qu'une copie
        // (celui qui l'a lache comme les autres) : il reste cinematique au fond de la cuve, et seuls ses collisionneurs etaient allumes --
        // ceux des maillons, eteints pendant le suivi. Personne ne pouvait plus reprendre le tuyau ; dans le jeu on saisit un maillon au
        // bord. Ici : maillons a moins de 2 m du bout saisissables tant que personne ne tient le tuyau et que le bout est sous le sol, et le
        // maillon que la main prend lui donne le bout (GiveEnd) -- le bout sort de la fosse, comme tire. Le bout n'est pas remonte dans la
        // cuve : le declencheur de la fosse descend avec le niveau, un bout plus haut arreterait la vidange. [Test] TuyauBord=0 : comme avant.
        static int bordOn = -1;
        static bool Bord { get { if (bordOn < 0) bordOn = Config.GetInt("Test", "TuyauBord", 1); return bordOn != 0; } }
        static bool rimOn, pitLast; static float rimNext, pitAt; static int rimLogs, giveLogs;
        static bool EndInPit()
        {
            float now = Time.realtimeSinceStartup;
            if (now < pitAt) return pitLast;
            pitAt = now + 0.5f;
            float g = GroundUnder(end.transform.position + Vector3.up * 3f);
            pitLast = g > float.MinValue && end.transform.position.y < g - 0.3f;
            return pitLast;
        }
        static void RimLinks(bool laid)
        {
            bool rim = Bord && laid && cols != null && EndInPit();
            float now = Time.realtimeSinceStartup;
            if (rim == rimOn && (!rim || now < rimNext)) return;
            rimOn = rim; rimNext = now + 0.5f;
            Vector3 e = end.transform.position;
            int n = 0;
            foreach (Collider c in cols)
            {
                if (c == null) continue;
                Rigidbody rb = c.GetComponentInParent<Rigidbody>();
                if (rb == null || rb == end) continue;
                Vector3 d = rb.position - e; d.y = 0f;
                bool on = rim && d.sqrMagnitude < 4f;
                if (c.enabled != on) c.enabled = on;
                if (on) n++;
            }
            if (rim && rimLogs++ < 6) Log.Info("tuyau de la GIFU : bout dans une fosse, " + n + " collisionneur(s) de maillon du bord saisissables");
        }
        // Maillon pris par la main du jeu (« Ragdoll picked » : joint de la main relie a son corps) : elle tient le bout a sa place -- bout amene
        // la ou etait le maillon, joint relie au bout, PickedObject = le bout ; la suite (tenu ici, HandFollow, reprise) est celle du bout.
        static void GiveEnd(GameObject link)
        {
            PlayMakerFSM hf = HandFsm();
            FsmGameObject po = hf != null ? hf.FsmVariables.FindFsmGameObject("PickedObject") : null;
            if (po == null || po.Value != link) return;
            Vector3 p = link.transform.position;
            end.transform.position = p; end.position = p;
            if (!end.isKinematic) { end.velocity = Vector3.zero; end.angularVelocity = Vector3.zero; }
            FixedJoint j = hf.GetComponent<FixedJoint>();
            if (j != null) j.connectedBody = end;
            po.Value = end.gameObject;
            if (giveLogs++ < 10) Log.Info("tuyau de la GIFU : maillon " + link.name + " saisi, la main tient le bout");
        }

        // Bout tenu ici sur une copie du camion (dossier #50, 06/11 : « chez l'hote le tuyau ne se prend pas ») : les maillons portent le tag RAGDOLL, la main du jeu les
        // tient par une attache (FixedJoint du corps Hand), sans les prendre pour enfants. Le bout est cinematique ici depuis fd224987 (27/10) : l'attache ne le bougeait
        // pas, il restait au sol, main fermee, tant que ce joueur ne menait pas la GIFU ; jamais vu, la main d'essai en etait le parent. Il suit donc la main, comme
        // l'attache le ferait ; tire au-dela de la chaine, il reste au bout et la main lache (attache cassee dans le jeu). [Test] TuyauMain=0 : comme avant.
        static bool handSet; static Vector3 handOff; static Quaternion handRot; static Transform handT;
        static void HandFollow(Vector3 a0)
        {
            if (Config.GetInt("Test", "TuyauMain", 1) == 0) return;
            if (handT == null) { GameObject hg = Game.PlayerPart("Pivot/AnimPivot/Camera/FPSCamera/1Hand_Assemble/Hand"); handT = hg != null ? hg.transform : null; }
            if (handT == null) return;
            if (!handSet) { handSet = true; handOff = handT.InverseTransformPoint(end.transform.position); handRot = Quaternion.Inverse(handT.rotation) * end.transform.rotation; }
            Vector3 want = handT.TransformPoint(handOff), d = want - a0;
            float L = chainLen > 1f ? chainLen : restLen;
            if (L > 1f && d.magnitude > L)
            {
                want = a0 + d.normalized * L;
                if (d.magnitude > L + 1f)
                {
                    PlayMakerFSM hf = Game.FsmOn(handT.gameObject, "PickUp");
                    if (hf != null) hf.SendEvent("PROCEED Drop");
                    if (logs++ < 10) Log.Info("tuyau de la GIFU : bout tire au-dela de la chaine (" + d.magnitude.ToString("F1") + " m) : la main lache");
                }
            }
            end.transform.position = want; end.transform.rotation = handT.rotation * handRot;
        }
        // Maillon tourne vers le suivant. Son axe de longueur n'est pas son z mais la pose du maillon suivant sous lui : tourne en z (LookRotation, depuis le
        // 12/10), chaque maillon etait de travers a 90 degres, un escalier en dents de scie chez tous ceux dont la chaine est posee par le code, jamais vu en
        // chiffres (dossiers #41, #46, #47, #50 : « tordu » ; premieres photos du tuyau, TuyauPhoto=1, 05/11), et rendu ainsi a la physique ses articulations
        // le remettaient d'un coup (« se detend », maillons a 15 m/s). [Test] TuyauAxe=0 : comme avant.
        static Vector3[] linkAxis; static int axeOn = -1;
        static bool Axe { get { if (axeOn < 0) axeOn = Config.GetInt("Test", "TuyauAxe", 1); return axeOn != 0; } }
        static bool simWas; static float stayGrace; static float shapeAt; static int shapeLogs;
        static void ShapeLog(Transform gt, string why)
        {
            if (shapeLogs++ < 40) Log.Info("tuyau de la GIFU : " + why + ", " + Role + (selfMode ? " par ici" : "") + ", sous le sol/coudes " + TestUnder() + ", bout a " + (end.transform.position - coupler.position).magnitude.ToString("F1") + " m du raccord, regime " + VehicleSync.EngineRpm(gt).ToString("F0"));
        }
        static void Aim(Transform t, int i, Vector3 dir)
        {
            if (!Axe || linkAxis == null || i >= linkAxis.Length) { t.rotation = Quaternion.LookRotation(dir, Vector3.up); return; }
            t.rotation = Quaternion.FromToRotation(t.rotation * linkAxis[i], dir) * t.rotation;
        }

        // Copie du bout (chaine posee d'apres l'autre) : elle ne touche plus les declencheurs des fosses d'ici. Sinon, a une pose un
        // peu differente de celle de l'autre, elle y entrait et en sortait, la fosse d'ici en suivant -- remise sans cesse a l'etat de
        // celui qui tient le tuyau (Jobs.FixWells), la pompe d'ici allumee une image sur deux. Rendu au rangement et a la prise en main.
        static readonly System.Collections.Generic.List<Collider> wellCols = new System.Collections.Generic.List<Collider>(), wellIgnored = new System.Collections.Generic.List<Collider>();
        static float nextWellCols;
        static void IgnoreWells(bool on)
        {
            if (cols == null || end == null) return;
            if (!on)
            {
                if (wellIgnored.Count == 0) return;
                foreach (Collider c in cols) if (c != null && c.transform.IsChildOf(end.transform)) foreach (Collider w in wellIgnored) if (w != null) Physics.IgnoreCollision(c, w, false);
                wellIgnored.Clear();
                return;
            }
            float now = Time.realtimeSinceStartup;
            if (now >= nextWellCols) { nextWellCols = now + 5f; wellCols.Clear(); Jobs.WellColliders(wellCols); }
            foreach (Collider w in wellCols)
            {
                if (w == null || (w.transform.position - end.transform.position).sqrMagnitude > 400f) continue;
                foreach (Collider c in cols) if (c != null && c.enabled && c.transform.IsChildOf(end.transform)) Physics.IgnoreCollision(c, w, true);   // (a refaire : un collisionneur eteint puis rallume l'oublie)
                if (!wellIgnored.Contains(w)) wellIgnored.Add(w);
            }
        }

        // Pose de la chaine (n maillons, longueur L) du raccord a au bout b : pendante (parabole) si son mou tient au-dessus du sol ;
        // sinon posee au sol -- descente sous le raccord, le long du sol, remontee au bout -- et la longueur en trop en boucle sur le
        // cote. Avant (27/10) le mou etait seulement borne au sol : bout pres du raccord, les 24 maillons (~6 m) tasses sur une
        // ligne courte, « tuyau replie en tube » chez le client (Markos, dossier #14, 28/10).
        // Dossier #41 (xZOTIK et Markos, 02/11 : « chez le client le tuyau est dans le sol, chez l'hote tordu ») : bout plonge dans la fosse,
        // GroundY(b) etait le fond de la fosse -- le plancher descendait sous le terrain, la parabole passait (son mou n'etait teste qu'au
        // milieu) et la chaine allait du raccord a la fosse a travers le sol ; et la longueur en trop faisait un detour a angles droits
        // sur le cote. Maintenant : parabole gardee seulement si aucun maillon n'est sous le sol, plancher pris sous le raccord, detour en
        // arc, et chaque maillon (sauf a 0,6 m du bout, qui entre dans la fosse) remonte au-dessus du sol sous lui. [Test] TuyauSolAncien=1 :
        // comme avant.
        public static bool LaidFromRemote { get { return following && !selfMode; } }
        public static Vector3 EndPos { get { return end != null ? end.transform.position : Vector3.zero; } }
        public static string Role { get { return (mine ? "a nous" : leading ? "mene ici" : remoteBy >= 0 ? "suivi #" + remoteBy : "physique") + (following ? " pose" : "") + (end != null && end.isKinematic ? " cin" : ""); } }

        // Forme de la chaine (dossiers #41 et #46, xZOTIK et Markos 03/11 : « apres que l'hote l'a ajuste, le tuyau est tantot normal,
        // tantot tordu » ; « quand le client le reprend, chez l'hote le tuyau devient tordu »). Chez celui qui suit, la chaine etait
        // recalculee (Layout) du raccord au bout recu : parabole ou chaine au sol avec sa boucle sur le cote selon la pose du bout, a
        // quelques centimetres pres -- pas la forme de la chaine physique de l'autre, et elle sautait d'une forme a l'autre. La source
        // (celui qui tient ou a lache le bout, ou le camion mene sans personne au bout) envoie maintenant ses maillons, dans le repere
        // du camion, 5 fois/s tenu, 2 fois/s pose s'ils bougent (toutes les 2 s sinon) ; ailleurs ils sont poses dessus.
        // [Test] TuyauChaine=0 : comme avant.
        static Vector3[] chainIn, chainOut, chainCur; static float chainAt, nextChain, chainKeep; static int chainBy = -1, chaineOn = -1;
        static bool Chaine { get { if (chaineOn < 0) chaineOn = Config.GetInt("Test", "TuyauChaine", 1); return chaineOn != 0; } }
        static int formeOn = -1; static Vector3[] keepPts; static Quaternion[] keepRot; static bool keepSet, chainMoving; static Vector3 keepAt, stayAt;
        // (rendue a la physique depuis une pose calculee, la chaine se detend d'un coup : freinee 1,5 s)
        static float dampUntil; static float[] dragSaved, angSaved;
        static void Damp()
        {
            if (segs == null || !Forme) return;
            if (dampUntil <= 0f) { dragSaved = new float[segs.Length]; angSaved = new float[segs.Length]; for (int i = 0; i < segs.Length; i++) { dragSaved[i] = segs[i].drag; angSaved[i] = segs[i].angularDrag; } }
            foreach (Rigidbody r in segs) if (r != null) { r.drag = 8f; r.angularDrag = 8f; }
            dampUntil = Time.realtimeSinceStartup + 1.5f;
        }
        static void Undamp()
        {
            dampUntil = 0f;
            if (segs == null || dragSaved == null || dragSaved.Length != segs.Length) return;
            for (int i = 0; i < segs.Length; i++) if (segs[i] != null) { segs[i].drag = dragSaved[i]; segs[i].angularDrag = angSaved[i]; }
        }

        static bool Forme { get { if (formeOn < 0) formeOn = Config.GetInt("Test", "TuyauForme", 1); return formeOn != 0; } }
        static int Cm(float v) { return Mathf.Clamp(Mathf.RoundToInt(v * 100f) + 32768, 0, 65535); }
        static float FromCm(int v) { return (v - 32768) * 0.01f; }
        static int Byte1(float v) { return Mathf.Clamp(Mathf.RoundToInt((v + 1f) * 127.5f), 0, 255); }
        static float From1(int v) { return v / 127.5f - 1f; }
        static Quaternion[] chainRot;

        static void SendChain(Transform gt, float now, bool held)
        {
            if (now < nextChain || segs == null || segs.Length == 0 || segs.Length > 200) return;
            int n = segs.Length;
            if (chainOut == null || chainOut.Length != n) { chainOut = new Vector3[n]; chainCur = new Vector3[n]; chainKeep = 0f; }
            float moved = 0f;
            for (int i = 0; i < n; i++) { chainCur[i] = gt.InverseTransformPoint(segs[i].position); moved = Mathf.Max(moved, (chainCur[i] - chainOut[i]).sqrMagnitude); }
            chainMoving = moved >= 0.0009f;
            if (moved < 0.0009f && now < chainKeep) return;
            nextChain = now + (held || (chainMoving && Forme) ? 0.2f : 0.5f); chainKeep = now + 2f;
            var w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@chaine").U8(n);
            Quaternion inv = Quaternion.Inverse(gt.rotation);
            for (int i = 0; i < n; i++)
            {
                chainOut[i] = chainCur[i]; w.U16(Cm(chainCur[i].x)).U16(Cm(chainCur[i].y)).U16(Cm(chainCur[i].z));
                Quaternion q = inv * segs[i].transform.rotation;   // (orientation aussi : recalculee ailleurs, le maillon ne ressemblait pas et la chaine se detendait d'un coup une fois rendue a la physique)
                w.U8(Byte1(q.x)).U8(Byte1(q.y)).U8(Byte1(q.z)).U8(Byte1(q.w));
            }
            Session.SendAll(w, false);
        }

        public static void OnChain(int who, NetReader r)
        {
            int n = r.U8();
            if (!Find() || !root.activeInHierarchy || mine || FireTools.Held() == end.gameObject || testHeld || segs == null || n != segs.Length) return;
            if (chainIn == null || chainIn.Length != n) chainIn = new Vector3[n];
            if (chainRot == null || chainRot.Length != n) chainRot = new Quaternion[n];
            for (int i = 0; i < n; i++)
            {
                float x = FromCm(r.U16()), y = FromCm(r.U16()); chainIn[i] = new Vector3(x, y, FromCm(r.U16()));
                float qx = From1(r.U8()), qy = From1(r.U8()), qz = From1(r.U8()), qw = From1(r.U8());
                float len = Mathf.Sqrt(qx * qx + qy * qy + qz * qz + qw * qw);
                chainRot[i] = len > 0.01f ? new Quaternion(qx / len, qy / len, qz / len, qw / len) : Quaternion.identity;
            }
            if (chainBy != who && logs++ < 10) Log.Info("tuyau de la GIFU : forme de la chaine de #" + who + " suivie (" + n + " maillons)");
            chainAt = Time.realtimeSinceStartup; chainBy = who;
        }
        static int solAncien = -1;
        static Vector3[] Layout(Vector3 a, Vector3 b, float L, int n)
        {
            if (solAncien < 0) solAncien = Config.GetInt("Test", "TuyauSolAncien", 0);
            if (solAncien != 0) return LayoutOld(a, b, L, n);
            var pts = new Vector3[n + 1];
            pts[0] = a;
            float d = (b - a).magnitude, sag = Mathf.Max(0f, L - d) * 0.5f;
            bool under = false;
            for (int i = 1; i <= n; i++)
            {
                float k = (float)i / n;
                pts[i] = Vector3.Lerp(a, b, k) + Vector3.down * (sag * 4f * k * (1f - k));
                if (!under && i < n && !NearEnd(pts[i], b) && pts[i].y < GroundUnder(pts[i]) + 0.02f) under = true;
            }
            if (L <= d * 1.02f || !under) { if (Axe) EvenSteps(pts); LiftAboveGround(pts, b); return pts; }
            float ga = GroundUnder(a), floor = (ga > float.MinValue ? ga : GroundY(a)) + 0.05f;
            Vector3 A = new Vector3(a.x, Mathf.Min(floor, a.y), a.z), B = new Vector3(b.x, Mathf.Max(floor, b.y), b.z);
            float lp = Mathf.Max(0f, a.y - A.y) + (B - A).magnitude + Mathf.Max(0f, B.y - b.y);
            var poly = new System.Collections.Generic.List<Vector3> { a, A };
            float extra = L - lp;
            Vector3 dir = B - A; dir.y = 0f;
            float D = dir.magnitude;
            if (extra > 0.05f)
            {
                Vector3 side = D > 1e-2f ? Vector3.Cross(Vector3.up, dir / D) : (Gifu() != null ? -Gifu().right : Vector3.right);
                if (D < 0.5f) { poly.Add(A + side * (extra * 0.5f)); poly.Add(B + side * (extra * 0.5f)); }
                else
                {
                    // arc en sinus sur le cote (longueur en plus ~ extra) au lieu de deux angles droits
                    float h = Mathf.Min(extra * 0.5f, 2f * D / Mathf.PI * Mathf.Sqrt(extra / D));
                    for (int j = 1; j < 8; j++) { float t = j / 8f; poly.Add(Vector3.Lerp(A, B, t) + side * (h * Mathf.Sin(Mathf.PI * t))); }
                }
            }
            poly.Add(B); poly.Add(b);
            float total = 0f;
            for (int i = 1; i < poly.Count; i++) total += (poly[i] - poly[i - 1]).magnitude;
            if (total < 1e-4f) { for (int i = 1; i <= n; i++) pts[i] = b; return pts; }
            int seg = 1; float acc = 0f;
            for (int i = 1; i <= n; i++)
            {
                float want = total * i / n;
                while (seg < poly.Count - 1 && acc + (poly[seg] - poly[seg - 1]).magnitude < want) { acc += (poly[seg] - poly[seg - 1]).magnitude; seg++; }
                float len = (poly[seg] - poly[seg - 1]).magnitude;
                pts[i] = len > 1e-5f ? Vector3.Lerp(poly[seg - 1], poly[seg], (want - acc) / len) : poly[seg];
            }
            LiftAboveGround(pts, b);
            return pts;
        }
        // Parabole : maillons a pas egaux le long de la courbe (a pas egaux sur la corde, tasses au milieu et ecartes aux extremites).
        static void EvenSteps(Vector3[] pts)
        {
            int n = pts.Length - 1;
            var src = (Vector3[])pts.Clone();
            float total = 0f;
            for (int i = 1; i <= n; i++) total += (src[i] - src[i - 1]).magnitude;
            if (total < 1e-4f) return;
            int seg = 1; float acc = 0f;
            for (int i = 1; i < n; i++)
            {
                float want = total * i / n;
                while (seg < n && acc + (src[seg] - src[seg - 1]).magnitude < want) { acc += (src[seg] - src[seg - 1]).magnitude; seg++; }
                float len = (src[seg] - src[seg - 1]).magnitude;
                pts[i] = len > 1e-5f ? Vector3.Lerp(src[seg - 1], src[seg], (want - acc) / len) : src[seg];
            }
        }
        static bool NearEnd(Vector3 p, Vector3 b) { float dx = p.x - b.x, dz = p.z - b.z; return dx * dx + dz * dz < 0.36f; }
        static void LiftAboveGround(Vector3[] pts, Vector3 b)
        {
            for (int i = 1; i < pts.Length - 1; i++)
            {
                if (NearEnd(pts[i], b)) continue;
                float g = GroundUnder(pts[i]) + 0.05f;
                if (pts[i].y < g) pts[i].y = g;
            }
        }
        // Sol sous un point, cherche d'au-dessus (un maillon deja sous le terrain : GroundY, qui part de 0,3 m au-dessus, ne le
        // trouvait pas) ; sans le tuyau, les joueurs ni la GIFU (le tuyau qui passe sous le camion reste sous lui).
        static float GroundUnder(Vector3 p)
        {
            float best = float.MaxValue, y = float.MinValue;
            foreach (RaycastHit h in Physics.RaycastAll(p + Vector3.up * 1.5f, Vector3.down, 8f, ~0))
            {
                if (h.collider == null || h.collider.isTrigger || (root != null && h.collider.transform.IsChildOf(root.transform)) || Game.UnderPlayer(h.collider.transform)) continue;
                Transform gt = Gifu();
                if (gt != null && h.collider.transform.IsChildOf(gt)) continue;
                if (h.distance < best) { best = h.distance; y = h.point.y; }
            }
            return y;
        }

        static Vector3[] LayoutOld(Vector3 a, Vector3 b, float L, int n)
        {
            var pts = new Vector3[n + 1];
            pts[0] = a;
            float d = (b - a).magnitude, sag = Mathf.Max(0f, L - d) * 0.5f;
            float floor = Mathf.Min(GroundY(a), GroundY(b)) + 0.05f;
            Vector3 mid = (a + b) * 0.5f;
            if (L <= d * 1.02f || mid.y - sag >= floor)
            {
                for (int i = 1; i <= n; i++) { float k = (float)i / n; pts[i] = Vector3.Lerp(a, b, k) + Vector3.down * (sag * 4f * k * (1f - k)); }
                return pts;
            }
            Vector3 A = new Vector3(a.x, floor, a.z), B = new Vector3(b.x, floor, b.z);
            float lp = Mathf.Max(0f, a.y - floor) + (B - A).magnitude + Mathf.Max(0f, b.y - floor);
            var poly = new System.Collections.Generic.List<Vector3> { a, A };
            float extra = L - lp;
            if (extra > 0.05f)
            {
                Vector3 dir = B - A; dir.y = 0f;
                Vector3 side = dir.sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, dir.normalized) : (Gifu() != null ? -Gifu().right : Vector3.right);
                float w = extra * 0.5f;
                poly.Add(A + side * w); poly.Add(B + side * w);
            }
            poly.Add(B); poly.Add(b);
            float total = 0f;
            for (int i = 1; i < poly.Count; i++) total += (poly[i] - poly[i - 1]).magnitude;
            if (total < 1e-4f) { for (int i = 1; i <= n; i++) pts[i] = b; return pts; }
            int seg = 1; float acc = 0f;
            for (int i = 1; i <= n; i++)
            {
                float want = total * i / n;
                while (seg < poly.Count - 1 && acc + (poly[seg] - poly[seg - 1]).magnitude < want) { acc += (poly[seg] - poly[seg - 1]).magnitude; seg++; }
                float len = (poly[seg] - poly[seg - 1]).magnitude;
                pts[i] = len > 1e-5f ? Vector3.Lerp(poly[seg - 1], poly[seg], (want - acc) / len) : poly[seg];
            }
            return pts;
        }
        // Sol sous un point (sans le tuyau ni les joueurs ; la GIFU compte : le tuyau peut reposer sur elle).
        static float GroundY(Vector3 p)
        {
            float best = float.MaxValue, y = p.y - 1f;
            foreach (RaycastHit h in Physics.RaycastAll(p + Vector3.up * 0.3f, Vector3.down, 6f, ~0))
            {
                if (h.collider == null || h.collider.isTrigger || (root != null && h.collider.transform.IsChildOf(root.transform)) || Game.UnderPlayer(h.collider.transform)) continue;
                if (h.distance < best) { best = h.distance; y = h.point.y; }
            }
            return y;
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
            bool wasSelf = selfMode; selfMode = false; dropSet = false;
            // (chaine etiree au-dela de sa longueur -- bout recu loin, lache par l'autre : rendue ainsi a la physique, ses joints
            // arrachaient le raccord, corps libre a la racine de la scene, et le tuyau restait a 12 m du camion chez l'hote ; essai
            // tuyau 31/10, Markos dossier #29). Le bout est d'abord ramene a portee du raccord, la chaine reposee jusqu'a lui.
            if (coupler != null && segs != null && segs.Length > 0 && restLen > 1f && FireTools.Held() != end.gameObject && Config.GetInt("Test", "TuyauEtire", 0) == 0)
            {
                Vector3 a = coupler.position, d = end.transform.position - a;
                if (d.magnitude > restLen * 1.05f)
                {
                    int n = segs.Length;
                    Vector3[] pts = Layout(a, a + d.normalized * restLen * 0.9f, restLen, n);
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 p = pts[i + 1], q = i + 2 <= n ? pts[i + 2] : pts[n] + (pts[n] - pts[n - 1]);
                        segs[i].transform.position = p;
                        if ((q - p).sqrMagnitude > 1e-6f) Aim(segs[i].transform, i, q - p);
                    }
                    if (logs++ < 10) Log.Info("tuyau de la GIFU : chaine etiree a " + d.magnitude.ToString("F1") + " m rendue a la physique : bout ramene a portee du raccord");
                }
            }
            // (rendus a la physique a moins d'un rayon du sol, les maillons entraient dans le terrain, a sens unique : ils passaient dessous. Du raccord au bout, parents d'abord. [Test] TuyauSol=0 : comme avant.)
            if (segs != null && Forme && Config.GetInt("Test", "TuyauSol", 1) != 0)
            {
                var want = new Vector3[segs.Length]; int lifted = 0;
                for (int i = 0; i < segs.Length; i++)
                {
                    want[i] = segs[i].transform.position;
                    float g = GroundUnder(want[i]);
                    if (g > float.MinValue && want[i].y < g + 0.12f) { want[i].y = g + 0.12f; lifted++; }
                }
                if (lifted > 0 && FireTools.Held() != end.gameObject)
                {
                    for (int i = 0; i < segs.Length; i++) segs[i].transform.position = want[i];
                    if (logs++ < 10) Log.Info("tuyau de la GIFU : rendu a la physique : " + lifted + " maillon(s) remontes au-dessus du sol");
                }
            }
            if (couplerRb != null) couplerRb.isKinematic = couplerKin;   // (colliders rallumes : IgnoreCollision a refaire)
            if (segs != null) foreach (Rigidbody r in segs) if (r != null) { if (wasSelf && r == end) { r.isKinematic = Forme && FireTools.Held() != end.gameObject && !testHeld ? false : endKin; if (!r.isKinematic) { r.velocity = Vector3.zero; r.angularVelocity = Vector3.zero; } continue; } r.isKinematic = false; r.velocity = Vector3.zero; r.angularVelocity = Vector3.zero; }
            if (cols != null) foreach (Collider c in cols) if (c != null) c.enabled = true;
            IgnoreWells(false);
            Damp();
        }

        // [Test] Autotest=fossepaie (2 joueurs, places loin de HouseShit3 par TestPos) : 40 s apres l'arrivee de l'autre,
        // l'hote passe la commande de HouseShit3 (niveau 4,95, ORDERTAKEN -> Full) ; 5 s apres l'invite va devant la maison ;
        // 15 s apres l'hote (autorite des fosses, GIFU garee) la vide (0,3 -> Pay) ; l'invite note a +25 s si le proprio est
        // la et tend l'argent (PayMoney allume), l'hote reste loin (maison hors de portee chez lui).
        static int tStep; static float tAt;
        static string TestWell { get { int m = Config.GetInt("Test", "TuyauMaison", 3); return Game.Msc ? "JOBS/HouseShit" + m + "/WasteWell_2000litre/Shit/Level" + m + "/ShitLevelTrigger" : "JOBS/HouseShit" + m + "/WasteWell_2000litre/Shit/Level/ShitLevelTrigger"; } }
        static string TestMan { get { return Game.Msc ? "JOBS/HouseShit3/LOD/ShitNPC/ShitMan3" : "JOBS/HouseShit3/LOD/ShitNPC/Man"; } }   // (MSC : Level{N}, ShitMan{N})
        // [Test] Autotest=tuyau (2 joueurs) : 40 s apres l'arrivee de l'autre, l'invite sort le tuyau (ActivateHose -> Remove
        // part, rejoue chez l'hote) puis promene le bout 10 s (comme tenu : il l'envoie) ; chacun note la pose du bout toutes
        // les 2 s ; a +16 s l'invite le range (Put hose back).
        static bool testHeld;
        static int fosseOn = -1, fStep; static float nextFosseLog; static Vector3 wellAt; static GameObject wellGo; static float nextNudge; static bool nudgeOut;
        static bool fosseTest { get { if (fosseOn < 0) fosseOn = Config.GetInt("Test", "TuyauDansFosse", 0); return fosseOn != 0; } }
        static float HoseBack { get { return Vidange ? 160f : Siege ? 110f : fosseTest ? 70f : 50f; } }
        // ([Test] TuyauSiege=1, dossiers #41/#47 : tuyau sorti par l'hote, personne au bout. +12 l'invite monte au volant, +20 il roule 4 s, +28 il descend ; +42 l'hote
        // monte, +50 il roule 4 s, +62 il descend ; +75 l'invite remonte et demarre, +90 il descend. TuyauTenu=1 en plus : l'invite a d'abord tenu le
        // bout. Chacun note chaque seconde la forme vue ici, dans le repere du camion : plus grand saut d'un maillon d'une image a l'autre, ecart
        // maxi entre maillons, coudes, et d'ou vient la pose.)
        // ([Test] TuyauVidange=1 avec TuyauDansFosse=1 et TuyauMaison=n, dossier #49 : la vraie fosse, pas deplacee. La GIFU est amenee a 5 m de la fosse, les deux joueurs a cote ; l'invite
        // tient le bout dans la fosse 10 s puis le lache (physique). L'hote : +14 au volant, +20 moteur, +22 pompe hydraulique, +60 (TuyauVidangeSort=0 : jamais) il descend, moteur tournant.
        // Chacun note toutes les 2 s le regime, la pompe, le niveau, la citerne, l'etat du declencheur et le bout.)
        static int vidOn = -1, vStep; static float vNextLog;
        static bool Vidange { get { if (vidOn < 0) vidOn = Config.GetInt("Test", "TuyauVidange", 0); return vidOn != 0; } }
        static string VidangeState(float k)
        {
            Transform g = Gifu(), st = g != null ? g.Find("ShitTank") : null, hy = g != null ? g.Find("Dashboard/Knobs/Hydraulics") : null;
            PlayMakerFSM pump = st != null ? Game.FsmOn(st.gameObject, "Pump") : null, waste = st != null ? Game.FsmOn(st.gameObject, "Waste") : null, hp = hy != null ? Game.FsmOn(hy.gameObject, "Pump") : null;
            PlayMakerFSM lv = wellGo != null ? Game.FsmOn(wellGo, "Level") : null, tr = wellGo != null ? Game.FsmOn(wellGo, "Trigger") : null;
            System.Func<PlayMakerFSM, string, string> b = (f, n) => { FsmBool v = f != null ? f.FsmVariables.FindFsmBool(n) : null; return v != null && v.Value ? "1" : "0"; };
            System.Func<PlayMakerFSM, string, string> fl = (f, n) => { FsmFloat v = f != null ? f.FsmVariables.FindFsmFloat(n) : null; return v != null ? v.Value.ToString("F2") : "?"; };
            return "+" + k.ToString("F0") + " s : regime " + (g != null ? VehicleSync.EngineRpm(g).ToString("F0") : "?") + ", pompe " + (pump != null ? pump.ActiveStateName : "?") + " (en marche " + b(pump, "PumpRunning") + ", dans la fosse " + b(pump, "HoseInShit") + ", raccorde " + b(pump, "HoseAttached") + ", aspire " + b(pump, "Sucking") + ", " + fl(pump, "LitresPerSecond") + " L/s)"
                + ", hydraulique " + (hp != null ? hp.ActiveStateName : "?") + ", niveau " + fl(lv, "ShitLevel") + ", citerne " + fl(waste, "Waste") + " L, declencheur " + (tr != null ? tr.ActiveStateName : "?")
                + (wellGo != null ? " a y=" + wellGo.transform.position.y.ToString("F2") : "") + ", bout a " + (end != null && wellGo != null ? (end.transform.position - wellGo.transform.position).ToString("F2") : "?") + (end != null && end.isKinematic ? " cin" : " v=" + (end != null ? end.velocity.magnitude.ToString("F2") : "?"))
                + (mine ? ", a nous" : leading ? ", mene ici" : remoteBy >= 0 ? ", suivi #" + remoteBy : ", physique") + (following ? " pose" : "") + ", passages " + Jobs.HoseSeen + ", annonces " + Jobs.HoseSent + ", recus " + Jobs.HoseGot;
        }
        static void VidangeTest(float k, float now)
        {
            if (now >= vNextLog) { vNextLog = now + 2f; Log.Info("autotest : tuyau, vidange " + VidangeState(k)); }
            // (TuyauVidangeInvite=1, l'ordre de Matilda Prime : l'invite monte a +0, demarre a +6, descend a +8 moteur tournant -- le camion reste a lui -- pendant qu'il tient le bout)
            if (!Session.IsHost && Config.GetInt("Test", "TuyauVidangeInvite", 0) != 0)
            {
                float[] gat = { 0f, 2f, 4f, 8f };
                if (vStep < gat.Length && k > gat[vStep])
                {
                    int gi = vStep++;
                    if (gi == 0) Log.Info("autotest : tuyau, vidange : l'invite va au volant, " + VehicleSync.TestEnter(TestCar, false));
                    else if (gi <= 2) Log.Info("autotest : tuyau, vidange : au volant, " + VehicleSync.TestEnter(TestCar, true));
                    else Log.Info("autotest : tuyau, vidange : l'invite descend, " + VehicleSync.TestExit(TestCar));
                }
                if (k > 6f && k < 15f) VehicleSync.TestForceRpm(TestCar, 600f);
            }
            if (!Session.IsHost) return;
            // (TuyauVidangeRegime : regime tenu de +20 a TuyauVidangeCale, 80 s par defaut -- ensuite le moteur cale, comme chez Matilda Prime)
            float rpm = Config.GetFloat("Test", "TuyauVidangeRegime", 2000f);
            if (rpm > 0f && k > 20f && k < Config.GetFloat("Test", "TuyauVidangeCale", 80f)) VehicleSync.TestForceRpm(TestCar, rpm);
            float[] at = { 14f, 16f, 18f, 20f, 22f, 60f };
            if (vStep >= at.Length || k <= at[vStep]) return;
            int i = vStep++;
            if (i == 0) Log.Info("autotest : tuyau, vidange : l'hote va au volant, " + VehicleSync.TestEnter(TestCar, false));
            else if (i <= 2) Log.Info("autotest : tuyau, vidange : au volant, " + VehicleSync.TestEnter(TestCar, true));
            else if (i == 3) Log.Info("autotest : tuyau, vidange : " + VehicleSync.TestStart(TestCar));
            else if (i == 4)
            {
                Transform g = Gifu(), hy = g != null ? g.Find("Dashboard/Knobs/Hydraulics") : null;
                PlayMakerFSM u = hy != null ? Game.FsmOn(hy.gameObject, "Use") : null;
                if (u != null) Game.SetState(u, "Test");
                Log.Info("autotest : tuyau, vidange : pompe hydraulique -> " + (u != null ? u.ActiveStateName : "introuvable"));
            }
            else if (Config.GetInt("Test", "TuyauVidangeSort", 1) != 0) Log.Info("autotest : tuyau, vidange : descend, " + VehicleSync.TestExit(TestCar));
        }
        static int siegeOn = -1, sStep; static float sAt, sJump, nextShape, sV, sGV; static Vector3 sG0; static Vector3[] sPrev; static int chainFrames, layoutFrames;
        static bool Siege { get { if (siegeOn < 0) siegeOn = Config.GetInt("Test", "TuyauSiege", 0); return siegeOn != 0; } }
        const string TestCar = "GIFU(750/450psi)";
        // ([Test] TuyauPhoto=1 avec TuyauSiege=1, dossiers #47/#50 : a chaque instant de pAt chacun note tous les maillons dans le repere du camion et photographie le tuyau, memes angles des deux cotes)
        static readonly float[] pAt = { 3f, 10f, 16f, 25f, 32f, 38f, 48f, 58f, 68f, 76f, 84f, 92f };
        static int pStep;
        static void PhotoTest(string tag, Transform g)
        {
            var sb = new System.Text.StringBuilder("autotest : tuyau, photo " + tag + " " + (Session.IsHost ? "hote" : "invite") + " " + TestUnder() + " :");
            Vector3 mid = (coupler.position + end.transform.position) * 0.5f;
            for (int i = 0; i < segs.Length; i++)
            {
                Transform t = segs[i].transform; Vector3 lp = g.InverseTransformPoint(t.position);
                Vector3 nx = i + 1 < segs.Length ? segs[i + 1].transform.position - t.position : t.position - segs[i - 1].transform.position;
                sb.Append(" ").Append(i).Append("=").Append(lp.x.ToString("F2")).Append(",").Append(lp.y.ToString("F2")).Append(",").Append(lp.z.ToString("F2")).Append("|").Append(Vector3.Angle(t.forward, nx).ToString("F0")).Append("|").Append(segs[i].isKinematic ? "k" : "p");
                mid = i == segs.Length / 2 ? t.position : mid;
            }
            if (linkAxis != null && segs.Length > 3) sb.Append(" axe range ").Append(linkAxis[1].ToString("F3")).Append(" axe ici ").Append(segs[2].transform.localPosition.ToString("F3")).Append(" longueur ").Append(restLen.ToString("F2"));
            Log.Info(sb.ToString());
            Shot(tag + "-dos", mid - g.forward * 6f + Vector3.up * 2.5f, mid);
            Shot(tag + "-haut", mid + Vector3.up * 8f + g.right * 0.5f, mid);
        }
        internal static void Shot(string name, Vector3 from, Vector3 at)
        {
            var go = new GameObject("tuyau_photo");
            try
            {
                var cam = go.AddComponent<Camera>(); cam.enabled = false;
                Camera main = PlayerSync.LocalCamera != null ? PlayerSync.LocalCamera.GetComponent<Camera>() : null;
                if (main != null) cam.CopyFrom(main);
                cam.fieldOfView = 55f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 300f;
                go.transform.position = from; go.transform.LookAt(at);
                // (photo lisible de nuit : lumiere d'essai le temps du rendu, l'heure de la partie ne change pas)
                var lg = new GameObject("tuyau_photo_lumiere"); var li = lg.AddComponent<Light>(); li.type = LightType.Directional; li.intensity = 1.2f; li.color = Color.white; li.shadows = LightShadows.None; lg.transform.rotation = Quaternion.Euler(55f, 30f, 0f);
                Color amb0 = RenderSettings.ambientLight; UnityEngine.Rendering.AmbientMode am0 = RenderSettings.ambientMode; bool fog0 = RenderSettings.fog;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.6f, 0.6f, 0.6f); RenderSettings.fog = false;
                var rt = new RenderTexture(960, 540, 24); cam.targetTexture = rt; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.5f, 0.6f, 0.75f); cam.Render();
                RenderSettings.ambientMode = am0; RenderSettings.ambientLight = amb0; RenderSettings.fog = fog0; Object.Destroy(lg);
                RenderTexture.active = rt; var tex = new Texture2D(960, 540, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); tex.Apply();
                RenderTexture.active = null; cam.targetTexture = null;
                string png = System.IO.Path.Combine(System.IO.Path.Combine(Log.DataDir, "dumps"), "tuyau-" + (Session.IsHost ? "hote" : "invite") + name + ".png");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(png));
                System.IO.File.WriteAllBytes(png, tex.EncodeToPNG());
                Object.Destroy(tex); rt.Release(); Object.Destroy(rt);
            }
            catch (System.Exception e) { Log.Warn("tuyau : photo " + name + " : " + e.Message); }
            Object.Destroy(go);
        }
        static void SiegeTest(float k, float now)
        {
            Transform g = Gifu();
            if (g != null && segs != null && root.activeInHierarchy)
            {
                if (sPrev == null || sPrev.Length != segs.Length) sPrev = new Vector3[segs.Length];
                else for (int i = 0; i < segs.Length; i++) sJump = Mathf.Max(sJump, (g.InverseTransformPoint(segs[i].transform.position) - sPrev[i]).magnitude);
                for (int i = 0; i < segs.Length; i++) sPrev[i] = g.InverseTransformPoint(segs[i].transform.position);
                if (sG0 == Vector3.zero) sG0 = g.position;
                foreach (Rigidbody r in segs) if (r != null && !r.isKinematic) sV = Mathf.Max(sV, r.velocity.magnitude);
                Rigidbody sgb = g.GetComponent<Rigidbody>(); if (sgb != null && !sgb.isKinematic) sGV = Mathf.Max(sGV, sgb.velocity.magnitude);
                if (now >= nextShape)
                {
                    nextShape = now + 1f;
                    float gap = 0f; Vector3 lp = coupler.position; foreach (Rigidbody r in segs) { gap = Mathf.Max(gap, (r.transform.position - lp).magnitude); lp = r.transform.position; }
                    Log.Info("autotest : tuyau, siege +" + k.ToString("F0") + " s : saut maxi " + sJump.ToString("F2") + " m, ecart maxi " + gap.ToString("F2") + " m, sous le sol/coudes " + TestUnder() + ", " + (mine ? "a nous" : leading ? "mene ici" : remoteBy >= 0 ? "suivi #" + remoteBy : "physique, rien") + (following ? (selfMode ? ", posee ici" : ", posee d'apres l'autre") : "") + (VehicleSync.SimAuthority(g) ? ", camion ici" : ", camion copie") + ", images chaine/calcul " + chainFrames + "/" + layoutFrames + ", raccord " + (coupler.position - g.position).magnitude.ToString("F2") + " m, vitesse maillons " + sV.ToString("F0") + " m/s, camion a " + (sG0 != Vector3.zero ? (g.position - sG0).magnitude : 0f).ToString("F2") + " m, vitesse camion " + sGV.ToString("F1") + " m/s, maillons " + ChainShape());
                    sJump = 0f; chainFrames = layoutFrames = 0; sV = 0f; sGV = 0f;
                }
            }
            if (g != null && segs != null && pStep < pAt.Length && k > pAt[pStep] && Config.GetInt("Test", "TuyauPhoto", 0) != 0) { PhotoTest("+" + pAt[pStep].ToString("F0"), g); pStep++; }
            bool host = Session.IsHost;
            // (TuyauSiegeHote=1 : l'hote a tenu puis lache le bout avant que l'invite monte)
            if (host && Config.GetInt("Test", "TuyauSiegeHote", 0) != 0)
            {
                if (hStep == 0 && k > 4f) { hStep = 1; TakeTest(); Log.Info("autotest : tuyau, siege : l'hote prend le bout"); }
                if (hStep == 1) testHand.position = g.position + g.right * 4f + g.forward * -4f;
                if (hStep == 1 && k > 8f) { hStep = 2; DropTest(); Log.Info("autotest : tuyau, siege : l'hote lache le bout"); }
            }
            // (TuyauSiegeTient=1 : l'invite tient le bout de +4 a +70, sans monter au volant, pendant que l'hote monte, roule et descend)
            bool tient = Config.GetInt("Test", "TuyauSiegeTient", 0) != 0;
            if (!host && tient)
            {
                if (hStep == 0 && k > 4f) { hStep = 1; TakeTest(); Log.Info("autotest : tuyau, siege : l'invite prend le bout"); }
                if (hStep == 1 && k > 70f) { hStep = 2; DropTest(); Log.Info("autotest : tuyau, siege : l'invite lache le bout"); }
                if (hStep == 1 && g != null && Config.GetInt("Test", "TuyauPhoto", 0) != 0) { Transform ha = g.Find("ActivateHose"); if (ha != null) { Vector3 to = ha.position + g.right * 3.5f - g.forward * 1.5f; float gy = GroundUnder(to); to.y = (gy > float.MinValue ? gy : to.y) + 1f; testHand.position = Vector3.MoveTowards(testHand.position, to, 2f * Time.deltaTime); } }
            }
            float[] at = host ? new[] { 42f, 44f, 46f, 62f } : tient ? new float[0] : new[] { 12f, 14f, 16f, 28f, 75f, 77f, 79f, 81f, 90f };
            if (sStep < at.Length && k > at[sStep])
            {
                int i = sStep++;
                int local = !host && i >= 4 ? i - 4 : i;
                if (local == 0) Log.Info("autotest : tuyau, siege : " + (host ? "l'hote" : "l'invite") + " va au volant, " + VehicleSync.TestEnter(TestCar, false));
                else if (local == 1 || local == 2) Log.Info("autotest : tuyau, siege : au volant, " + VehicleSync.TestEnter(TestCar, true));
                else if (!host && i == 7) Log.Info("autotest : tuyau, siege : " + VehicleSync.TestStart(TestCar));
                else Log.Info("autotest : tuyau, siege : descend, " + VehicleSync.TestExit(TestCar));
            }
            if (!host && !tient && k > 20f && k < 24f) { Rigidbody gb = VehicleSync.Body(TestCar); if (gb != null) { Vector3 f = gb.transform.forward; f.y = 0f; gb.velocity = f.normalized * 3f + Vector3.up * Mathf.Min(gb.velocity.y, 0f); } }
            if (host && k > 50f && k < 54f) { Rigidbody gb = VehicleSync.Body(TestCar); if (gb != null) { Vector3 f = gb.transform.forward; f.y = 0f; gb.velocity = f.normalized * 3f + Vector3.up * Mathf.Min(gb.velocity.y, 0f); } }
        }
        // ([Test] TuyauEchange=1 avec TuyauDansFosse=1, dossiers #41/#46 : la main passe de l'un a l'autre, le bout pres de la fosse. Chez
        // l'invite la fosse est decalee de TuyauDecalage m (3 par defaut) sur le cote -- poses un peu differentes d'un jeu a l'autre :
        //  +2..+12 l'invite tient le bout dans SA fosse puis le lache ; +15 l'hote le prend sur place (hors de SA fosse), +19 le lache ;
        //  +26 l'invite le reprend sur place (dans la sienne), +34 le lache ; +30 l'invite fausse le niveau de la fosse ici (-1 ou +1).
        // Chacun note a +13, +18, +24, +30, +33 et +38 l'etat des fosses, la pompe et trois maillons dans le repere du camion.)
        static int echangeOn = -1, hStep, eLog; static Vector3 wellOff;
        static bool Echange { get { if (echangeOn < 0) echangeOn = Config.GetInt("Test", "TuyauEchange", 0); return fosseTest && echangeOn != 0; } }
        static readonly float[] eAt = { 13f, 18f, 24f, 30f, 33f, 38f };
        // [Test] Autotest=tuyau TuyauPrise=1 (dossier #50, 06/11 : « chez l'hote le tuyau ne se prend pas ») : prise par le VRAI chemin du jeu, l'automate
        // PickUp de la main (rayon sur les couches 19, 15, 9, RaycastHitObject, « State 1 », puis son « PROCEED Drop »), pas par la main d'essai. Seul le
        // clic n'est pas rejoue. TuyauPriseSuite : temps:h|g:p|d;... (p prend, d lache).
        static bool Prise { get { return Config.GetInt("Test", "TuyauPrise", 0) != 0; } }
        static bool pFoto, pHeure; static int pFotoN; static float pPose; static float walkUntil, walkLog, pActAt; static Transform walkT; static Vector3 walkP0, walkDir; static string pAct; static int priseStep, pIdx; static float pLog, pAfter; static string[] pSeq;
        // (dossier #55, essai -- TuyauPriseSuite, avec TuyauDansFosse=1 TuyauVidange=1 : la vraie fosse) f : le bout pose ici est mis au fond de
        // la cuve, comme lache au-dessus de l'ouverture ; b : ce joueur va au bord de la fosse (TuyauBordDist m, cote camion) et s'accroupit ;
        // c : clic depuis SA camera avec la portee de la main du jeu (1 m) -- le collisionneur du tuyau le plus proche, s'il est a portee et
        // que le rayon y arrive, est donne a la main comme par MousePick ; sinon « IMPOSSIBLE ».
        static void PriseFosse()
        {
            if (wellGo == null) { Log.Info("autotest : tuyau, FOSSE : pas de fosse (TuyauDansFosse=1 TuyauVidange=1)"); return; }
            Vector3 w = wellGo.transform.position;
            // (couvercle de la fosse ote d'abord, comme les joueurs -- « well cover deplacee » chez Markos ; ferme, le bout se posait dessus)
            Transform well = wellGo.transform;
            while (well != null && !well.name.StartsWith("WasteWell")) well = well.parent;
            Transform cover = null;
            if (well != null) foreach (Transform t in well.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("well cover")) { cover = t; break; }
            if (cover != null)
            {
                Transform gq = Gifu(); Vector3 away = gq != null ? w - gq.position : Vector3.right; away.y = 0f; away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.right;
                Vector3 cp = cover.position + away * 1.6f; cp.y = GroundY(cp + Vector3.up * 3f) + 0.15f;
                Rigidbody cb = cover.GetComponent<Rigidbody>();
                cover.position = cp; if (cb != null) { cb.position = cp; if (!cb.isKinematic) cb.velocity = Vector3.zero; }
                Log.Info("autotest : tuyau, FOSSE : couvercle " + cover.name + " ote, pose en " + cp.ToString("F1"));
            }
            end.transform.position = w;
            if (following && selfMode) { Ground(); dropPos = end.transform.position; dropRot = end.transform.rotation; dropSet = true; }
            else { end.position = w; if (!end.isKinematic) end.velocity = Vector3.zero; }
            float g = GroundUnder(end.transform.position + Vector3.up * 3f);
            Log.Info("autotest : tuyau, FOSSE " + (Session.IsHost ? "hote" : "invite") + " : bout mis dans " + Recon.Path(wellGo.transform) + ", a "
                     + (g > float.MinValue ? (g - end.transform.position.y).ToString("F2") : "?") + " m sous le sol, " + Role);
        }
        static void PriseBord()
        {
            GameObject pl = Game.PlayerGo; var cc = pl != null ? pl.GetComponent<CharacterController>() : null;
            if (cc == null) return;
            Vector3 w = wellGo != null ? wellGo.transform.position : end.transform.position;
            // (y3/y4 du 08/11 : pris du cote du camion, le bord tombait sur la GIFU garee pres de la fosse -- camera a 3,47 m du sol.) Bord
            // pris depuis la fosse : a cote du maillon qui passe le bord (a hauteur du sol, le plus proche du centre), sur le cote,
            // perpendiculaire a la chaine, a TuyauBordDist m de ce maillon -- la ou un joueur s'accroupit pour attraper le tuyau.
            float gw = GroundY(w + Vector3.up * 3f);
            Vector3 rim = w; float best = float.MaxValue;
            if (segs != null) foreach (var s in segs)
            {
                if (s == null) continue;
                Vector3 sp = s.transform.position, dh = sp - w; dh.y = 0f;
                if (Mathf.Abs(sp.y - gw) > 0.5f) continue;
                if (dh.magnitude < best) { best = dh.magnitude; rim = sp; }
            }
            Vector3 dir = rim - w; dir.y = 0f; dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.right;
            Vector3 q = rim + Vector3.Cross(Vector3.up, dir) * Config.GetFloat("Test", "TuyauBordDist", 0.6f);
            Vector3 dq = q - w; dq.y = 0f;   // (jamais au-dessus de l'ouverture : au moins 0,9 m du centre de la fosse)
            if (dq.magnitude < 0.9f) { q = w + (dq.sqrMagnitude > 0.01f ? dq.normalized : Vector3.Cross(Vector3.up, dir)) * 0.9f; }
            q.y = GroundY(q + Vector3.up * 3f) + 1f;
            cc.enabled = false; pl.transform.position = q; cc.enabled = true;
            PlayMakerFSM cf = Game.FindFsm("PLAYER", "Crouch");
            if (cf != null) Game.SetState(cf, "Move down 2");
            Log.Info("autotest : tuyau, BORD " + (Session.IsHost ? "hote" : "invite") + " : au bord de la fosse en " + q.ToString("F1") + " (maillon du bord a "
                     + (best < float.MaxValue ? best.ToString("F2") + " m du centre" : "? : aucun maillon a hauteur du sol") + ", sol " + (q.y - 1f).ToString("F2") + "), accroupi -> " + (cf != null ? cf.ActiveStateName : "?"));
        }
        static void PriseCamera()
        {
            PlayMakerFSM hf = HandFsm(); Transform cam = PlayerSync.LocalCamera;
            if (hf == null || cam == null || cols == null) { Log.Info("autotest : tuyau, CLIC camera : pas de main ou de camera"); return; }
            Vector3 eye = cam.position; Collider best = null; float bd = float.MaxValue;
            int mask = (1 << 19) | (1 << 15) | (1 << 9);
            foreach (Collider c in cols)
            {
                if (c == null || !c.enabled || !c.gameObject.activeInHierarchy || (mask & (1 << c.gameObject.layer)) == 0) continue;
                float d = (c.ClosestPointOnBounds(eye) - eye).magnitude;
                if (d < bd) { bd = d; best = c; }
            }
            float gy = GroundUnder(eye);
            string where = "camera a " + (gy > float.MinValue ? (eye.y - gy).ToString("F2") : "?") + " m du sol";
            string who = Session.IsHost ? "hote" : "invite";
            if (best == null || bd > 1f)
            {
                Log.Info("autotest : tuyau, CLIC camera IMPOSSIBLE " + who + " : collisionneur du tuyau le plus proche a " + (best != null ? bd.ToString("F2") + " m" : "aucun") + " (portee 1 m), " + where + ", " + PriseState());
                return;
            }
            RaycastHit hit;
            Vector3 to = best.ClosestPointOnBounds(eye);
            bool ok = Physics.Raycast(eye, (to - eye).normalized, out hit, 1.05f, mask);
            if (!ok || hit.collider.attachedRigidbody == null || !hit.collider.transform.IsChildOf(root.transform))
            {
                Log.Info("autotest : tuyau, CLIC camera " + who + " : rayon arrete par " + (ok ? Recon.Path(hit.collider.transform) : "rien") + ", " + where);
                return;
            }
            FsmGameObject rh = hf.FsmVariables.FindFsmGameObject("RaycastHitObject");
            if (rh != null) { rh.Value = hit.collider.gameObject; Game.SetState(hf, "State 1"); }
            Log.Info("autotest : tuyau, CLIC camera " + who + " -> " + hit.collider.name + (hit.collider.attachedRigidbody == end ? " (le bout)" : " (maillon)") + " a " + hit.distance.ToString("F2") + " m, " + where);
            pAfter = Time.realtimeSinceStartup + 0.5f;
        }
        static PlayMakerFSM HandFsm() { GameObject hg = Game.PlayerPart("Pivot/AnimPivot/Camera/FPSCamera/1Hand_Assemble/Hand"); return hg != null ? Game.FsmOn(hg, "PickUp") : null; }
        static string PriseState()
        {
            PlayMakerFSM hf = HandFsm(); FsmGameObject po = hf != null ? hf.FsmVariables.FindFsmGameObject("PickedObject") : null;
            int on = 0; if (cols != null) foreach (Collider c in cols) if (c != null && c.enabled && c.gameObject.activeInHierarchy) on++;
            string ec = ""; if (end != null) foreach (Collider c in end.GetComponents<Collider>()) ec += (c.enabled ? "on" : "OFF") + (c.isTrigger ? "/declencheur " : " ");
            return "main " + (hf != null ? hf.ActiveStateName : "?") + ", tient " + (po != null && po.Value != null ? po.Value.name : "rien") + (FireTools.Held() == end.gameObject ? " (le bout)" : "")
                   + " ; bout couche " + end.gameObject.layer + " tag " + end.tag + " " + (end.isKinematic ? "cin" : "libre") + " parent " + (end.transform.parent != null ? end.transform.parent.name : "aucun") + " coll " + ec
                   + "a " + (end.transform.position.y - GroundUnder(end.transform.position)).ToString("F2") + " m du sol, " + (end.transform.position - coupler.position).magnitude.ToString("F1") + " m du raccord ; "
                   + on + "/" + cols.Length + " coll ; " + (mine ? "a nous" : "") + (remoteBy >= 0 ? " suivi #" + remoteBy : "") + (following ? (selfMode ? " suivi-soi" : " suivi") : " physique") + (remoteHeld ? " tenu-la-bas" : "") + (leading ? " mene" : "");
        }
        static void PriseGo(GameObject pl)
        {
            var cc = pl.GetComponent<CharacterController>(); if (cc == null) return;
            Transform gq = Gifu(); Vector3 away = gq != null ? end.transform.position - gq.position : Vector3.right; away.y = 0f; away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.right;
            Vector3 q = end.transform.position + away * 1.5f; q.y = GroundY(q + Vector3.up * 1.2f) + 1f;
            cc.enabled = false; pl.transform.position = q; cc.enabled = true;
        }
        static void PriseClick()
        {
            GameObject pl = Game.PlayerGo; PlayMakerFSM hf = HandFsm();
            if (pl == null || hf == null) { Log.Info("autotest : tuyau, prise : pas de main"); return; }
            if ((pl.transform.position - end.transform.position).magnitude > 3f) PriseGo(pl);
            Vector3 p = end.transform.position, from = p + new Vector3(0.35f, 0.5f, 0.2f);
            RaycastHit hit;
            bool ok = Physics.Raycast(from, (p - from).normalized, out hit, 1f, (1 << 19) | (1 << 15) | (1 << 9));
            // (y3/y4 du 08/11 : rayon arrete par l'herbe -- MAP/MESH/TERRAIN_OBJ/Grass2 -- au seul angle essaye : comme un joueur, viser le bout
            // sous d'autres angles, le premier qui le touche)
            if (!ok || hit.collider.attachedRigidbody != end)
            {
                Vector3[] offs = { new Vector3(-0.35f, 0.5f, -0.2f), new Vector3(0f, 0.6f, 0f), new Vector3(0.6f, 0.25f, 0f), new Vector3(-0.6f, 0.25f, 0f), new Vector3(0f, 0.25f, 0.6f), new Vector3(0f, 0.25f, -0.6f) };
                foreach (Vector3 o in offs)
                {
                    RaycastHit h2;
                    if (Physics.Raycast(p + o, -o.normalized, out h2, o.magnitude + 0.3f, (1 << 19) | (1 << 15) | (1 << 9)) && h2.collider.attachedRigidbody == end)
                    { hit = h2; ok = true; Log.Info("autotest : tuyau, PRISE : bout vise sous un autre angle " + o.ToString("F2")); break; }
                }
            }
            Log.Info("autotest : tuyau, PRISE " + (Session.IsHost ? "hote" : "invite") + " : avant " + PriseState());
            if (!ok)
            {
                RaycastHit any; bool a2 = Physics.Raycast(from, (p - from).normalized, out any, 1f);
                Log.Info("autotest : tuyau, PRISE IMPOSSIBLE : le rayon de la main ne touche rien" + (a2 ? " (sans masque : " + Recon.Path(any.collider.transform) + " couche " + any.collider.gameObject.layer + ")" : " (sans masque non plus)"));
                return;
            }
            FsmGameObject rh = hf.FsmVariables.FindFsmGameObject("RaycastHitObject");
            bool isEnd = hit.collider.attachedRigidbody == end;
            Log.Info("autotest : tuyau, PRISE rayon -> " + hit.collider.name + (isEnd ? " (le bout)" : " (PAS le bout)") + " couche " + hit.collider.gameObject.layer + " tag " + hit.collider.tag + ", etat main " + hf.ActiveStateName);
            if (rh != null) { rh.Value = hit.collider.gameObject; Game.SetState(hf, "State 1"); }
            pAfter = Time.realtimeSinceStartup + 0.5f;
            walkUntil = Time.realtimeSinceStartup + Config.GetFloat("Test", "TuyauMarche", 4f); walkT = hit.collider.transform; walkP0 = walkT.position; walkLog = 0f;
            walkDir = end.transform.position - (Gifu() != null ? Gifu().position : coupler.position); walkDir.y = 0f; walkDir = walkDir.sqrMagnitude > 0.01f ? walkDir.normalized : Vector3.forward;
        }
        static void PriseTest(float k, float now)
        {
            if (!Find()) return;
            Transform at = Gifu() != null ? Gifu().Find("ActivateHose") : null;
            PlayMakerFSM use = at != null ? Game.FsmOn(at.gameObject, "Use") : null;
            if (Session.IsHost && !pHeure && k > 1f && Config.GetInt("Test", "TuyauPriseHeure", 0) > 0)
            {
                pHeure = true; GameObject sunG = Game.FindAny("MAP/SUN/Pivot/SUN"); PlayMakerFSM sun = sunG != null ? Game.FsmOn(sunG, "Color") : null;
                if (sun != null) { sun.FsmVariables.GetFsmInt("Time").Value = Config.GetInt("Test", "TuyauPriseHeure", 13); sun.SendEvent("TIMESKIP"); }
                Log.Info("autotest : tuyau, prise : heure " + (sun != null ? sun.FsmName + " -> " + sun.ActiveStateName : "introuvable"));
            }
            if (pPose > 0f && now >= pPose && Gifu() != null) { pPose = 0f; PhotoTest("pose" + pFotoN, Gifu()); pFoto = false; }
            if (priseStep == 0 && use != null) { priseStep = 1; Game.SetState(use, "Remove part"); Log.Info("autotest : tuyau, prise : sorti -> " + use.ActiveStateName); }
            if (pSeq == null) pSeq = Config.Get("Test", "TuyauPriseSuite", "6:h:p;11:h:d;16:h:p;21:h:d;26:g:p;32:g:d;38:h:p;43:h:d;48:g:p;54:h:p;58:g:d;64:h:p;69:h:d").Split(';');
            while (pIdx < pSeq.Length)
            {
                string[] e = pSeq[pIdx].Split(':');
                if (e.Length < 3 || k < float.Parse(e[0], System.Globalization.CultureInfo.InvariantCulture)) break;
                pIdx++;
                if ((e[1] == "h") != Session.IsHost) continue;
                if (e[2] == "a") { GameObject ap = Game.PlayerGo; if (ap != null) PriseGo(ap); continue; }
                if (e[2] == "v") { Log.Info("autotest : tuyau, a cote du volant -> " + VehicleSync.TestEnter(TestCar, false)); pAct = "au volant"; pActAt = now + 2f; continue; }
                if (e[2] == "x") { Log.Info("autotest : tuyau, sort du volant -> " + VehicleSync.TestExit(TestCar)); continue; }
                if (e[2] == "f") { PriseFosse(); continue; }
                if (e[2] == "b") { PriseBord(); continue; }
                if (e[2] == "c") { PriseCamera(); continue; }
                if (e[2] == "p") PriseClick();
                else { PlayMakerFSM hf = HandFsm(); if (hf != null) hf.SendEvent("PROCEED Drop"); if (Config.GetInt("Test", "TuyauPhoto", 0) != 0) pPose = now + 2f; Log.Info("autotest : tuyau, LACHE " + (Session.IsHost ? "hote" : "invite") + " -> main " + (hf != null ? hf.ActiveStateName : "?")); pAfter = now + 0.5f; }
            }
            if (walkUntil > 0f)
            {
                GameObject wp = Game.PlayerGo; var wc = wp != null ? wp.GetComponent<CharacterController>() : null;
                if (wc != null && wc.enabled) wc.Move((walkDir + Vector3.down * 2f) * Time.deltaTime);
                if (now >= walkLog)
                {
                    walkLog = now + 1f;
                    PlayMakerFSM hw = HandFsm(); Rigidbody wr = walkT != null ? walkT.GetComponent<Rigidbody>() : null; Transform cw = PlayerSync.LocalCamera;
                    Log.Info("autotest : tuyau, MARCHE " + (Session.IsHost ? "hote" : "invite") + " : joueur " + (wp != null ? wp.transform.position.ToString("F1") : "?") + ", prise deplacee de " + (walkT != null ? (walkT.position - walkP0).magnitude.ToString("F2") : "?") + " m"
                             + (wr != null ? (wr.isKinematic ? " cin" : " libre") + " v " + (wr.isKinematic ? 0f : wr.velocity.magnitude).ToString("F1") : "") + ", camera a " + (cw != null && walkT != null ? (cw.position - walkT.position).magnitude.ToString("F2") : "?") + " m, main " + (hw != null ? hw.ActiveStateName + " en " + hw.transform.position.ToString("F1") : "?") + " bout " + end.transform.position.ToString("F1") + (mine ? " a nous" : "") + (following ? (selfMode ? " suivi-soi" : " suivi") : " physique") + ", bout-raccord " + (end.transform.position - coupler.position).magnitude.ToString("F1") + ", sol " + (end.transform.position.y - GroundUnder(end.transform.position)).ToString("F2") + ", sous le sol/coudes " + TestUnder());
                }
                if (!pFoto && walkUntil - now < 1.5f && Config.GetInt("Test", "TuyauPhoto", 0) != 0 && Gifu() != null) { pFoto = true; PhotoTest("tenu" + (++pFotoN), Gifu()); }
                if (now >= walkUntil) walkUntil = 0f;
            }
            if (pAct != null && now >= pActAt)
            {
                string act = pAct; pAct = null;
                Log.Info("autotest : tuyau, " + act + " -> " + VehicleSync.TestEnter(TestCar, true));
            }
            if (pAfter > 0f && now >= pAfter) { pAfter = 0f; Log.Info("autotest : tuyau, APRES " + (Session.IsHost ? "hote" : "invite") + " : " + PriseState()); }
            if (now >= pLog && k > 3f) { pLog = now + 3f; Log.Info("autotest : tuyau, etat " + (Session.IsHost ? "hote" : "invite") + " : " + PriseState()); }
        }
        static void TakeTest()
        {
            Release(); remoteBy = -1; testHeld = true; end.isKinematic = true;
            testHand = new GameObject("MWCoopTestHand").transform; testHand.position = end.transform.position; endParent = end.transform.parent; end.transform.parent = testHand;
        }
        static void DropTest()
        {
            testHeld = false; end.isKinematic = false;
            if (testHand != null) { end.transform.parent = endParent; Object.Destroy(testHand.gameObject); testHand = null; }
        }
        static string ChainShape()
        {
            Transform g = Gifu();
            if (g == null || segs == null || segs.Length < 18) return "?";
            return g.InverseTransformPoint(segs[5].transform.position).ToString("F2") + " " + g.InverseTransformPoint(segs[11].transform.position).ToString("F2") + " " + g.InverseTransformPoint(segs[17].transform.position).ToString("F2")
                   + " bout " + g.InverseTransformPoint(end.transform.position).ToString("F2");
        }
        static void EchangeTest(float k)
        {
            if (Session.IsHost && end != null)
            {
                if (hStep == 0 && k > 15f) { hStep = 1; TakeTest(); Log.Info("autotest : tuyau, echange : l'hote prend le bout"); }
                if (hStep == 1 && k > 19f) { hStep = 2; DropTest(); Log.Info("autotest : tuyau, echange : l'hote lache le bout"); }
            }
            if (!Session.IsHost && k > 30f && eLog == 3)
            {
                string part = TestWell + ":Level.ShitLevel";
                string st = Fluids.TestState(part);
                float lv; int eq = st.LastIndexOf("= ");
                bool up = eq >= 0 && float.TryParse(st.Substring(eq + 2), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out lv) && lv < 1.5f;
                Log.Info("autotest : tuyau, echange : niveau fausse ici : " + Fluids.TestBump(part, up ? 1f : -1f));
            }
            if (eLog < eAt.Length && k > eAt[eLog])
            {
                eLog++;
                Log.Info("autotest : tuyau, echange +" + eAt[eLog - 1].ToString("F0") + " s : " + Jobs.WellsNear(EndPos) + " ; " + Fluids.SepticState() + " ; " + Fluids.TestState(TestWell + ":Level.ShitLevel")
                         + " ; " + (mine ? "a nous" : remoteBy >= 0 ? "suivi sur #" + remoteBy : "?") + (LaidFromRemote ? ", chaine posee d'apres l'autre" : "") + " ; maillons 6/12/18 " + ChainShape());
            }
        }
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
            // ([Test] TuyauDansFosse=1, dossier #41 sur MWC : le declencheur de la fosse de HouseShit3 est amene a +25 s, chez les deux, au sol a
            // 3 m du raccord de la GIFU -- la ou l'invite pose le bout ; deplacer la GIFU l'emballait. L'invite y tient le bout 10 s puis l'y
            // lache (en le bougeant, plus bas), range a +70 s ; chacun note ses passages dans/hors de la fosse et ses annonces)
            if (fosseTest && fStep == 0 && Jobs.OtherInGame(25f))
            {
                fStep = 1;
                wellGo = Game.FindAny(TestWell);
                Transform g = Gifu(), ha = g != null ? g.Find("ActivateHose") : null;
                if (ha != null) { wellAt = ha.position + ha.right * 3f; wellAt.y = GroundY(wellAt + Vector3.up * 3f) + 0.2f; }
                if (Vidange && wellGo != null)
                {
                    wellAt = wellGo.transform.position;
                    // (TuyauVidangePleine=1 : fosse pleine, commande passee, comme chez Matilda Prime)
                    if (Session.IsHost && Config.GetInt("Test", "TuyauVidangePleine", 0) != 0)
                    {
                        PlayMakerFSM lf = Game.FsmOn(wellGo, "Level"); FsmFloat lvl = lf != null ? lf.FsmVariables.FindFsmFloat("ShitLevel") : null;
                        if (lvl != null) { lvl.Value = 4.9f; lf.SendEvent("ORDERTAKEN"); Log.Info("autotest : tuyau, vidange : fosse pleine, commande passee -> " + lf.ActiveStateName); }
                    }
                    // ([Test] TuyauVidangeRecul : part de ces 5 m gardee -- a 1, le raccord est a ~8 m de la fosse, hors de portee de la chaine
                    // posee a la main, dossier #55)
                    Vector3 park = wellAt + new Vector3(-5f, 0f, 2f) * Config.GetFloat("Test", "TuyauVidangeRecul", 1f); park.y = GroundY(park + Vector3.up * 3f) + 1.5f;
                    if (Session.IsHost && g != null) Log.Info("autotest : tuyau, vidange : " + VehicleSync.TestPlaceAt(TestCar, park, g.rotation));
                    GameObject pl = Game.PlayerGo; var cc = pl != null ? pl.GetComponent<CharacterController>() : null;
                    if (cc != null) { cc.enabled = false; Vector3 me = wellAt + new Vector3(1.5f, 0f, Session.IsHost ? 1.5f : -1.5f); me.y = GroundY(me + Vector3.up * 3f) + 1f; pl.transform.position = me; cc.enabled = true; }
                }
                if (ha != null && Echange) { Vector3 o = ha.right * Config.GetFloat("Test", "TuyauDecalage", 3f); Vector3 q = wellAt + o; q.y = GroundY(q + Vector3.up * 3f) + 0.2f; wellOff = q - wellAt; }
                Log.Info("autotest : tuyau, fosse " + (wellGo == null ? "introuvable" : ha == null ? "trouvee, raccord introuvable" : "amenee en " + wellAt.ToString("F1") + (wellGo.activeInHierarchy ? "" : " (eteinte)")));
            }
            if (Vidange && fStep == 1 && wellGo != null) wellAt = wellGo.transform.position;
            else if (fosseTest && fStep == 1 && wellGo != null && wellAt != Vector3.zero) wellGo.transform.position = wellAt + (Session.IsHost ? Vector3.zero : wellOff);   // (chaque image : l'automate du niveau deplace la fosse)
            if (fosseTest && fStep == 1 && now >= nextFosseLog) { nextFosseLog = now + 10f; Log.Info("autotest : tuyau, fosse : passages ici " + Jobs.HoseSeen + ", annonces " + Jobs.HoseSent + ", recus " + Jobs.HoseGot + (LaidFromRemote ? " (chaine posee d'apres l'autre)" : "")); }
            if (tStep == 0 && Jobs.OtherInGame(40f)) { tStep = 1; tAt = now; }
            if (tStep == 0) return;
            if (Prise) { PriseTest(now - tAt, now); return; }
            if (Echange) EchangeTest(now - tAt);
            if (Vidange) VidangeTest(now - tAt, now);
            if (Siege && Find()) SiegeTest(now - tAt, now);
            // (TuyauDepart=1 : l'hote, au volant, roule 6 s a +22 s -- apres le lacher de l'invite a +12 s ; [MWC] 28/10)
            if (Session.IsHost && Config.GetInt("Test", "TuyauDepart", 0) != 0 && tvStep == 6 && now - tAt > 22f) { tvStep = 7; tvAt = now; Log.Info("autotest : tuyau, la GIFU part (6 s)"); }
            if (tvStep == 7)
            {
                Rigidbody gb2 = VehicleSync.Body("GIFU(750/450psi)");
                if (gb2 != null && now - tvAt < 6f) { Vector3 f = gb2.transform.forward; f.y = 0f; gb2.velocity = f.normalized * 4f + Vector3.up * Mathf.Min(gb2.velocity.y, 0f); }
                if (now - tvAt >= 6f) { tvStep = 8; if (gb2 != null) gb2.velocity = Vector3.zero; Log.Info("autotest : tuyau, la GIFU s'arrete"); }
            }
            Transform gt = Gifu(), at = gt != null ? gt.Find("ActivateHose") : null;
            GameObject ah = at != null ? at.gameObject : null;
            PlayMakerFSM use = ah != null ? Game.FsmOn(ah, "Use") : null;
            // (sortie et rangement poses des deux cotes : en vrai c'est le clic, rejoue par Jobs)
            if (Session.IsHost && use != null)
            {
                if (tStep == 1) { tStep = 2; Game.SetState(use, "Remove part"); }
                if (tStep == 2 && now - tAt > HoseBack) { tStep = 3; Game.SetState(use, "Put hose back"); }
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
                    // ([Test] TuyauFosse=m, dossier #41 : bout tenu m metres sous le raccord a cote du camion, comme plonge dans une fosse)
                    float fd = Config.GetFloat("Test", "TuyauFosse", 0f);
                    if (testHand != null) testHand.position = fosseTest && wellAt != Vector3.zero ? wellAt + wellOff : fd > 0f ? basePos + Vector3.down * fd : basePos + new Vector3(Mathf.Sin(k) * 2f, 0.5f, Mathf.Cos(k) * 2f);
                    if (k > 10f && !(Vidange && Config.GetInt("Test", "TuyauVidangeTient", 0) != 0)) { tStep = 4; testHeld = false; end.isKinematic = false; if (testHand != null) { end.transform.parent = endParent; Object.Destroy(testHand.gameObject); testHand = null; } }
                }
                // (TuyauDansFosse=1 : bout pose qui bouge un peu -- comme la physique chez MSC -- 1,5 m dans/hors de la fosse chaque seconde ; chez
                // l'hote la chaine se repose d'apres lui a chaque fois)
                if (!Echange && !Vidange && fosseTest && tStep == 4 && end != null && wellAt != Vector3.zero && now - tAt < HoseBack - 3f && now >= nextNudge)
                {
                    nextNudge = now + 1f; nudgeOut = !nudgeOut;
                    Transform ha = ah.transform;
                    end.transform.position = wellAt + (nudgeOut ? ha.right * 1.5f : Vector3.zero); end.position = end.transform.position;
                }
                if (Echange && tStep == 4 && now - tAt > 26f && end != null) { tStep = 6; TakeTest(); Log.Info("autotest : tuyau, echange : l'invite reprend le bout"); }
                if (Echange && tStep == 6 && now - tAt > 34f && end != null) { tStep = 7; DropTest(); Log.Info("autotest : tuyau, echange : l'invite lache le bout"); }
                if ((tStep == 4 || tStep == 7) && now - tAt > HoseBack && use != null) { tStep = 5; Game.SetState(use, "Put hose back"); Log.Info("autotest : tuyau, range -> " + use.ActiveStateName); }
            }
            if (Find() && now >= nextTestLog && now - tAt < HoseBack + 6f)
            {
                nextTestLog = now + 4f;
                float vmax = 0f, dmax = 0f;
                if (segs != null && coupler != null) foreach (Rigidbody r in segs) if (r != null) { vmax = Mathf.Max(vmax, r.velocity.magnitude); dmax = Mathf.Max(dmax, (r.position - coupler.position).magnitude); }
                float lsum = 0f, lmin = float.MaxValue, ymin = float.MaxValue; Vector3 lp = coupler != null ? coupler.position : Vector3.zero;
                if (segs != null) foreach (Rigidbody r in segs) if (r != null) { float dd = (r.transform.position - lp).magnitude; lsum += dd; lmin = Mathf.Min(lmin, dd); lp = r.transform.position; ymin = Mathf.Min(ymin, lp.y); }
                if (segs != null && segs.Length > 0) Log.Info("autotest : tuyau, maillons : ecart moyen " + (lsum / segs.Length).ToString("F2") + " m, min " + lmin.ToString("F2") + " m, longueur " + lsum.ToString("F1") + " m, plus bas " + ymin.ToString("F2") + (following ? " (suivi)" : ""));
                Transform stt = Gifu() != null ? Gifu().Find("ShitTank") : null; Rigidbody stb = stt != null ? stt.GetComponent<Rigidbody>() : null;
                if (stb != null) Log.Info("autotest : tuyau, ShitTank : corps a " + (stb.position - stt.position).magnitude.ToString("F2") + " m de son objet" + (stb.isKinematic ? " (cin)" : " (libre)") + ", raccord a " + (coupler != null ? (coupler.position - stt.position).magnitude.ToString("F2") : "?") + " m");
                Log.Info("autotest : tuyau, maillons sous le sol " + TestUnder() + " (hors 0,6 m du bout), sol sous le bout " + (end != null ? GroundUnder(end.transform.position).ToString("F2") : "?"));
                Log.Info("autotest : tuyau, maillons : vitesse max " + vmax.ToString("F1") + " m/s, ecart max au raccord " + dmax.ToString("F1") + " m" + (following ? " (suivi)" : "") + (ignoring ? " (chocs copie coupes)" : ""));
                Log.Info("autotest : tuyau, " + (root.activeInHierarchy ? "sorti" : "range") + ", bout en " + end.position.ToString("F2") + (end.isKinematic ? " (cinematique)" : "") + (mine ? " a nous" : remoteBy >= 0 ? " suivi sur #" + remoteBy : "") + (following ? ", chaine posee" : "") + (cols != null && cols.Length > 0 && cols[0] != null && !cols[0].enabled ? ", sans collision" : "") + ", bout " + (EndGrabbable() ? "saisissable" : "insaisissable"));
            }
        }
        // Essai : maillons a plus de 10 cm sous le sol sous eux (sauf pres du bout, qui peut entrer dans une fosse), et coudes de plus de 60 degres.
        static string TestUnder()
        {
            if (segs == null || end == null) return "?";
            int under = 0, bends = 0;
            Vector3 b = end.transform.position, prev = coupler != null ? coupler.position : segs[0].position, prevDir = Vector3.zero;
            foreach (Rigidbody r in segs)
            {
                if (r == null) continue;
                Vector3 p = r.transform.position;
                if (r != end && !NearEnd(p, b)) { float g = GroundUnder(p); if (g == float.MinValue || p.y < g - 0.1f) under++; }   // (rien sous lui dans les 6,5 m : il est deja sous le terrain)
                Vector3 dir = p - prev;
                if (dir.sqrMagnitude > 1e-4f) { if (prevDir != Vector3.zero && Vector3.Angle(prevDir, dir) > 60f) bends++; prevDir = dir; }
                prev = p;
            }
            return under + "/" + segs.Length + ", coudes > 60 deg " + bends;
        }
        static float nextTestLog, tvAt; static int tvStep;
        static bool EndGrabbable() { if (cols != null) foreach (Collider c in cols) if (c != null && c.enabled && !c.isTrigger && c.GetComponentInParent<Rigidbody>() == end) return true; return false; }
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
            if (FireTools.Held() == end.gameObject || testHeld) return;   // (on le tient ici : le notre fait foi)
            if (remoteBy != who && logs++ < 10) Log.Info("tuyau de la GIFU : bout suivi sur #" + who + (held ? " (en main)" : " (pose)"));
            if (float.IsNaN(p.x + p.y + p.z) || float.IsInfinity(p.x + p.y + p.z) || (p - (coupler != null ? coupler.position : p)).sqrMagnitude > 900f) return;   // (rien de fou : 30 m au plus du camion)
            mine = false; remoteBy = who; rp = p; rq = q; lastRemote = Time.realtimeSinceStartup; remoteHeld = held;
        }
    }
}
