using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Barrages de police (TRAFFIC/Police). Chaque jour l'automate Police tire au sort s'il y a des controles et ou
    // (aeroport, piste de ski, champs : SendRandomEvent), y accroche (Parenting : SetParent) les deux agents a
    // l'alcootest, les deux voitures, la herse et les deux radars, puis allume Checkpoints. Tire chez chacun, le
    // barrage etait ailleurs (ou absent) chez l'autre et ses agents invisibles a l'autre conducteur.
    //  - L'hote decide : il envoie (fiable, a chaque changement et toutes les 10 s) Checkpoints allume ou non et,
    //    pour chacun des 7 elements, son parent, sa pose locale et s'il est allume. L'invite coupe son automate
    //    Police et recopie.
    //  - Les voitures de police sont de la circulation (Traffic, conteneur TRAFFIC/Police) : leur pose et leur
    //    poursuite sont celles de l'hote ; le corps des agents suit l'hote (Npcs), sauf quand un agent controle
    //    un invite (Npcs le lui confie : alcootest, amende).
    // Restent PERSONNELS (chacun les siens, rien n'est envoye) : alcootest et amende (le joueur controle paie), radars
    // (CopRadar::Speedtrap flashe le joueur local), crimes et recherche (PlayerWanted, Systems), prison (JAIL), la
    // fuite a un barrage d'un invite (sa voiture de police est la copie de celle de l'hote). Les agents a domicile
    // (COPS), allumes chez le joueur recherche, sont montres aux autres par Npcs tant qu'ils sont a 80 m de lui.
    public static class Police
    {
        // Variables de l'automate Police qui designent les elements accroches au lieu du jour.
        static readonly string[] Vars = { "CopAlc1", "CopAlc2", "CopCar1", "CopCar2", "CopCheckpoint", "CopRadar1", "CopRadar2" };
        const int Kind = 10;   // genre du message de controle de Traffic
        static PlayMakerFSM logic;
        static GameObject checkpoints;
        static Transform[] items;
        static bool built, muted;
        static float buildAt = -1, nextCheck, nextFull, nextLog;
        static string lastSig;
        static int sent, recv;

        public static void OnLevelLoaded()
        {
            logic = null; checkpoints = null; items = null; built = muted = false; lastSig = null; sent = recv = 0; step = 0;
            buildAt = PlayerSync.InGame ? Time.realtimeSinceStartup + 7f : -1;
        }

        static void Build()
        {
            built = true;
            GameObject p = Game.FindAny("TRAFFIC/Police");
            logic = p != null ? Game.FsmOn(p, "Police") : null;
            if (logic == null) { Log.Warn("police : TRAFFIC/Police::Police introuvable"); return; }
            FsmGameObject cg = logic.FsmVariables.FindFsmGameObject("Checkpoints");
            checkpoints = cg != null && cg.Value != null ? cg.Value : Game.FindAny("TRAFFIC/Police/Checkpoints");
            items = new Transform[Vars.Length];
            int found = 0;
            for (int i = 0; i < Vars.Length; i++)
            {
                FsmGameObject v = logic.FsmVariables.FindFsmGameObject(Vars[i]);
                if (v != null && v.Value != null) { items[i] = v.Value.transform; found++; }
            }
            Log.Info("police : barrage " + (checkpoints != null ? "trouve" : "absent") + ", " + found + "/" + Vars.Length + " elements, automate " + logic.ActiveStateName);
        }

        public static void Update()
        {
            float now = Time.realtimeSinceStartup;
            if (!built) { if (buildAt > 0 && now >= buildAt) Build(); return; }
            if (logic == null || !Session.IsHost || Session.RemoteCount == 0 || now < nextCheck) return;
            nextCheck = now + 0.5f;
            string sig = Signature();
            if (sig == lastSig && now < nextFull) return;
            lastSig = sig; nextFull = now + 10f;
            Send();
        }

        // Ce qui compte du barrage : allume, et pour chaque element son parent, allume, place (au decimetre).
        static string Signature()
        {
            var sb = new System.Text.StringBuilder(checkpoints != null && checkpoints.activeSelf ? "1" : "0");
            foreach (Transform t in items)
            {
                if (t == null) { sb.Append("|-"); continue; }
                Vector3 p = t.localPosition;
                sb.Append('|').Append(t.parent != null ? t.parent.name : "").Append(t.gameObject.activeSelf ? '+' : '-')
                  .Append((int)(p.x * 10)).Append(',').Append((int)(p.y * 10)).Append(',').Append((int)(p.z * 10));
            }
            return sb.ToString();
        }

        // [U16 0xFFFF][U8 10][U8 Checkpoints allume][U8 n] puis par element [Str chemin du parent][Vec pose locale]
        // [Quat rotation locale][U8 allume].
        static void Send()
        {
            var w = new NetWriter(Msg.Traffic).U16(0xFFFF).U8(Kind).U8(checkpoints != null && checkpoints.activeSelf ? 1 : 0).U8(items.Length);
            foreach (Transform t in items)
            {
                if (t == null) { w.Str("").Vec(Vector3.zero).Quat(Quaternion.identity).U8(0); continue; }
                w.Str(t.parent != null ? Recon.Path(t.parent) : "").Vec(t.localPosition).Quat(t.localRotation).U8(t.gameObject.activeSelf ? 1 : 0);
            }
            Session.Broadcast(w, true);
            if (++sent <= 10) Log.Info("police : barrage envoye (" + lastSig + ")");
        }

        // Invite : barrage de l'hote recopie (automate Police coupe a la premiere reception).
        public static void OnMessage(Peer from, NetReader r)
        {
            if (Session.IsHost || !built || logic == null) return;
            bool on = r.U8() != 0;
            int n = r.U8();
            if (!muted) { muted = true; logic.enabled = false; Log.Info("police : barrages dictes par l'hote (automate Police coupe ici)"); }
            if (checkpoints != null && checkpoints.activeSelf != on) checkpoints.SetActive(on);
            for (int i = 0; i < n; i++)
            {
                string pp = r.Str();
                Vector3 lp = r.Vec();
                Quaternion lr = r.Quat();
                bool act = r.U8() != 0;
                if (items == null || i >= items.Length || items[i] == null || pp.Length == 0) continue;
                Transform t = items[i];
                // Voiture suivie par Traffic : seulement son parent (sa place vient du trafic de l'hote).
                bool car = Traffic.Follows(t);
                if (t.parent == null || Recon.Path(t.parent) != pp)
                {
                    GameObject g = Game.FindAny(pp);
                    if (g != null) t.SetParent(g.transform, car);
                }
                if (!car) { t.localPosition = lp; t.localRotation = lr; }
                if (!car && t.gameObject.activeSelf != act) t.gameObject.SetActive(act);   // (voiture : allumee par Traffic)
            }
            if (++recv <= 10 || Time.realtimeSinceStartup >= nextLog)
            {
                nextLog = Time.realtimeSinceStartup + 60f;
                Log.Info("police : barrage de l'hote recopie (" + (on ? "allume" : "eteint") + ", " + Describe() + ")");
            }
        }

        static string Describe()
        {
            if (items == null) return "?";
            Transform a = items[0];
            return Vars[0] + (a != null ? " sous " + (a.parent != null ? a.parent.name : "-") + " en " + a.position.ToString("F1") + (a.gameObject.activeInHierarchy ? " actif" : " inactif") : " ?");
        }

        // Essai 'police' : [Test] PoliceForcer=1 -> a 30 s l'hote relance le tirage (Police "State 1" : lieu au
        // hasard, Parenting, Cop1 allume) ; les deux cotes notent le barrage toutes les 2 s.
        static int step;
        static float nextTest;

        public static void Test(string mode, float t)
        {
            if (mode != "police") return;
            if (Session.IsHost && step == 0 && t > 30f && logic != null && Config.GetInt("Test", "PoliceForcer", 0) != 0)
            {
                step = 1;
                string before = logic.ActiveStateName;
                Game.SetState(logic, "State 1");
                Log.Info("autotest : police : tirage relance (" + before + " => " + logic.ActiveStateName + ")");
            }
            float now = Time.realtimeSinceStartup;
            if (t < 20f || now < nextTest) return;
            nextTest = now + 2f;
            Log.Info("autotest : police barrage (" + (Session.IsHost ? "hote" : "invite") + ") automate " + (logic != null ? (logic.enabled ? logic.ActiveStateName : "coupe") : "?")
                     + ", checkpoints " + (checkpoints != null && checkpoints.activeInHierarchy) + ", " + Describe()
                     + ", voiture1 " + (items != null && items[2] != null ? items[2].position.ToString("F1") : "?") + ", envoyes " + sent + ", recus " + recv);
        }
    }
}
