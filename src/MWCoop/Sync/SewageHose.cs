using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Tuyau de la GIFU (fosses septiques). "TAKE HOSE" (GIFU/ActivateHose 'Use', rejoue chez tous par Jobs) active
    // GIFU/HOSE : un raccord fixe au camion et une chaine de segments Hose(xxxxx) ; le joueur tient le DERNIER segment
    // et le plonge dans la fosse. Ce bout n'etait suivi nulle part : chez les autres il pendait au bout de la copie du
    // camion (a-coups du reseau) et la chaine s'envolait (retour de gamereggo, 11/10 : « le tuyau bugue et vole partout »).
    // Celui qui l'a eu en main en dernier envoie la pose du bout (10 fois/s tenu, 2 fois/s pose) ; ailleurs le bout
    // devient cinematique a cette pose, le reste de la chaine pend entre le camion et lui. Rendu physique 3 s apres le
    // dernier message, et quand le tuyau est range.
    public static class SewageHose
    {
        static GameObject root;
        static Rigidbody end;
        static bool mine, wasOut;
        static int remoteBy = -1;
        static float nextSend, lastRemote, nextLook;
        static Vector3 rp; static Quaternion rq;
        static int logs;

        static Transform Gifu() { Rigidbody b = VehicleSync.Body("GIFU(750/450psi)"); return b != null ? b.transform : null; }

        public static void OnLevelLoaded() { root = null; end = null; mine = false; wasOut = false; remoteBy = -1; nextLook = 0; npcDone.Clear(); nextNpcLook = 0; }

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
            for (int i = 0; i < 200; i++) { Transform n = t.Find("Hose(xxxxx)"); if (n == null) break; t = n; }
            end = t != c ? t.GetComponent<Rigidbody>() : null;
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
                if (wasOut) { if (remoteBy >= 0) end.isKinematic = false; mine = false; remoteBy = -1; wasOut = false; }
                return;
            }
            wasOut = true;
            bool held = FireTools.Held() == end.gameObject || testHeld;
            if (held && !mine) { mine = true; if (remoteBy >= 0) { remoteBy = -1; if (!testHeld) end.isKinematic = false; } }
            if (mine && Session.RemoteCount > 0 && now >= nextSend)
            {
                nextSend = now + (held ? 0.1f : 0.5f);
                Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@bout").Vec(end.position).Quat(end.rotation).Bool(held), false);
            }
            if (remoteBy >= 0)
            {
                if (now - lastRemote > 3f) { end.isKinematic = false; remoteBy = -1; return; }
                if (!end.isKinematic) end.isKinematic = true;
                end.MovePosition(Vector3.Lerp(end.position, rp, 0.5f));
                end.MoveRotation(Quaternion.Slerp(end.rotation, rq, 0.5f));
            }
        }

        // [Test] Autotest=fossepaie (2 joueurs, places loin de HouseShit3 par TestPos) : 40 s apres l'arrivee de l'autre,
        // l'hote passe la commande de HouseShit3 (niveau 4,95, ORDERTAKEN -> Full) ; 5 s apres l'invite va devant la maison ;
        // 15 s apres l'hote (autorite des fosses, GIFU garee) la vide (0,3 -> Pay) ; l'invite note a +25 s si le proprio est
        // la et tend l'argent (PayMoney allume), l'hote reste loin (maison hors de portee chez lui).
        static int tStep; static float tAt;
        const string TestWell = "JOBS/HouseShit3/WasteWell_2000litre/Shit/Level/ShitLevelTrigger", TestMan = "JOBS/HouseShit3/LOD/ShitNPC/Man";
        // [Test] Autotest=tuyau (2 joueurs) : 40 s apres l'arrivee de l'autre, l'invite sort le tuyau (ActivateHose -> Remove
        // part, rejoue chez l'hote) puis promene le bout 10 s (comme tenu : il l'envoie) ; chacun note la pose du bout toutes
        // les 2 s ; a +16 s l'invite le range (Put hose back).
        static bool testHeld;
        static void TestHose(float now)
        {
            if (tStep == 0 && Jobs.OtherInGame(40f)) { tStep = 1; tAt = now; }
            if (tStep == 0) return;
            Transform gt = Gifu(), at = gt != null ? gt.Find("ActivateHose") : null;
            GameObject ah = at != null ? at.gameObject : null;
            PlayMakerFSM use = ah != null ? Game.FsmOn(ah, "Use") : null;
            // (sortie et rangement poses des deux cotes : en vrai c'est le clic, rejoue par Jobs)
            if (Session.IsHost && use != null)
            {
                if (tStep == 1) { tStep = 2; Game.SetState(use, "Remove part"); }
                if (tStep == 2 && now - tAt > 16f) { tStep = 3; Game.SetState(use, "Put hose back"); }
            }
            if (!Session.IsHost)
            {
                if (tStep == 1 && use != null) { tStep = 2; Game.SetState(use, "Remove part"); Log.Info("autotest : tuyau, sorti -> " + use.ActiveStateName); }
                if (tStep == 2 && now - tAt > 2f && Find()) { tStep = 3; testHeld = true; end.isKinematic = true; }
                if (tStep == 3 && end != null)
                {
                    float k = now - tAt - 2f;
                    Vector3 basePos = ah.transform.position + ah.transform.right * 3f;
                    end.MovePosition(basePos + new Vector3(Mathf.Sin(k) * 2f, 0.5f, Mathf.Cos(k) * 2f));
                    if (k > 10f) { tStep = 4; testHeld = false; end.isKinematic = false; }
                }
                if (tStep == 4 && now - tAt > 16f && use != null) { tStep = 5; Game.SetState(use, "Put hose back"); Log.Info("autotest : tuyau, range -> " + use.ActiveStateName); }
            }
            if (Find() && now >= nextTestLog && now - tAt < 20f)
            {
                nextTestLog = now + 2f;
                Log.Info("autotest : tuyau, " + (root.activeInHierarchy ? "sorti" : "range") + ", bout en " + end.position.ToString("F2") + (end.isKinematic ? " (cinematique)" : "") + (mine ? " a nous" : remoteBy >= 0 ? " suivi sur #" + remoteBy : ""));
            }
        }
        static float nextTestLog;

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
                GameObject pl = GameObject.Find("PLAYER");
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
            mine = false; remoteBy = who; rp = p; rq = q; lastRemote = Time.realtimeSinceStartup;
            if ((end.position - p).sqrMagnitude > 25f) { end.isKinematic = true; end.position = p; end.rotation = q; }   // (loin : d'un coup)
        }
    }
}
