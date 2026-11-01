using System.Collections.Generic;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Usine (Futufon, JOBS/FACTORY) : le travail visible et partage, comme les autres boulots (JD, 01/11 : « il faut
    // completement syncro le travail a Futufon »). Avant, le travail etait a chacun : les caisses des bacs et les pieces
    // qu'elles creent n'existaient que chez celui qui travaillait, l'autre le voyait seulement « jeter des objets par terre »
    // (Донской, 01/11).
    // Travail du jeu (releve du 01/11) : cinq bacs (JOB/PickChargers, PickManuals, PickSheets, PickTrays, PickBoxes, automate
    // 'Use') ; « Pick BOX » sort la caisse unique du bac (variable Package : chargers box(Clone)...) et la met en main. Dans
    // la caisse (automate 'Use' ; 'Data' pour la caisse a colis), « Open box » l'ouvre et « Pick item » cree une piece
    // (CreateObject depuis LOD1/Hidden : charger, manual, plastic tray...) et decompte Items/Pos.
    // Etape 1 (ici) : ces automates sont a ce module (ni recopies ni rejoues par le monde) ;
    //  - chaque caisse est suivie par Props sous une cle fixe (usine:<bac>) : sa pose, qui la tient ;
    //  - « Pick BOX » chez l'un : la caisse est allumee et montree dans la main de celui-la chez les autres ;
    //  - etats de la caisse (Open box, Pick item, State 1, State 3) rejoues sur la meme caisse chez les autres ;
    //  - chaque piece creee recoit une cle reseau (usine:<joueur>:<n>), donnee a la piece creee par le rejeu chez les autres
    //    (memes compteurs, meme piece) : Props suit ensuite sa pose et qui la tient.
    // Jobs (@usine) : U8 joueur, Str "@usine", U8 genre (1 : caisse sortie du bac, 2 : etat de la caisse), U8 bac, Str etat, Str cle.
    // [Test] UsineAncienne=1 : comme avant (travail a chacun).
    public static class Usine
    {
        const string Mod = "usine";
        const string Root = "JOBS/FACTORY/OpeningTimes/LOD1/JOB";
        static readonly string[] Bins = { "PickChargers", "PickManuals", "PickSheets", "PickTrays", "PickBoxes" };
        static readonly string[] BoxStates = { "Open box", "Pick item", "State 1", "State 3" };

        class Bin { public int Index; public PlayMakerFSM Use, Box; public GameObject BoxGo; public bool Hooked, BoxHooked; }
        static readonly Bin[] bins = new Bin[5];
        static float nextScan = -1;
        static int made;
        static string pendingKey;   // (rejeu de « Pick item » : la cle de la piece que l'autre a creee)

        public static bool On { get { return !Game.Msc && Config.GetInt("Test", "UsineAncienne", 0) == 0; } }

        public static void OnLevelLoaded()
        {
            for (int i = 0; i < bins.Length; i++) bins[i] = null;
            nextScan = PlayerSync.InGame ? Time.realtimeSinceStartup + 8f : -1;   // (avant le releve du monde, 16 s)
            made = 0; pendingKey = null;
        }

        class Hook : ModHook
        {
            public override string Module { get { return Mod; } }
            public Bin B; public string State; public bool Box, After;
            public override void OnEnter()
            {
                try { if (Session.Active && Replay.Depth == 0) OnLocal(this); else if (After && Replay.Depth > 0) OnReplayed(this); }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        public static void Update()
        {
            if (!On || nextScan < 0 || !Session.Active) return;
            float now = Time.realtimeSinceStartup;
            if (now < nextScan) return;
            bool all = true;
            for (int i = 0; i < Bins.Length; i++)
            {
                if (bins[i] == null || !bins[i].Hooked) { if (!TryBin(i)) { all = false; continue; } }
                if (!bins[i].BoxHooked && !HookBox(bins[i])) all = false;
            }
            nextScan = now + (all ? 30f : 1f);   // (caisse eteinte au bac : son automate n'est pret qu'une fois sortie)
        }

        static bool TryBin(int i)
        {
            GameObject bg = Game.FindAny(Root + "/" + Bins[i]);
            PlayMakerFSM use = bg != null ? Game.FsmOn(bg, "Use") : null;
            if (use == null) return false;
            FsmGameObject pv = use.FsmVariables.FindFsmGameObject("Package");
            GameObject box = pv != null ? pv.Value : null;
            if (box == null) return false;
            PlayMakerFSM bu = Game.FsmOn(box, "Use") ?? Game.FsmOn(box, "Data");
            if (use.Fsm == null || !use.Fsm.Initialized) return false;
            var b = bins[i] ?? (bins[i] = new Bin { Index = i });
            b.Use = use; b.Box = bu; b.BoxGo = box;
            if (!Replay.Claim(use, Mod)) Log.Warn("usine : " + Bins[i] + "::Use deja pris par " + Replay.Owner(use));
            if (bu != null && !Replay.Claim(bu, Mod)) Log.Warn("usine : " + box.name + "::" + bu.FsmName + " deja pris par " + Replay.Owner(bu));
            FsmState pick = use.Fsm.GetState("Pick BOX");
            if (pick != null) FsmHooks.Prepend(pick, new Hook { B = b, State = "Pick BOX" });
            Props.Adopt(box, "usine:" + Bins[i]);
            b.Hooked = true;
            Log.Info("usine : bac " + Bins[i] + " suivi (caisse " + box.name + (box.activeInHierarchy ? "" : ", eteinte") + ")");
            return true;
        }

        // Etats de la caisse accroches (une fois son automate pret : caisse allumee au moins une fois).
        static bool HookBox(Bin b)
        {
            if (b.Box == null || b.Box.FsmName != "Use") { b.BoxHooked = true; return true; }   // (caisse a colis : 'Data', etape 2)
            if (b.Box.Fsm == null || !b.Box.Fsm.Initialized) return false;
            int n = 0;
            foreach (string s in BoxStates)
            {
                FsmState st = b.Box.Fsm.GetState(s);
                if (st == null) continue;
                if (s == "Pick item")
                {
                    // (juste apres le CreateObject : l'IntCompare de la fin quitte l'etat, une action ajoutee en dernier ne tournait pas)
                    int at = -1;
                    for (int k = 0; k < st.Actions.Length; k++) if (st.Actions[k] is CreateObject) { at = k + 1; break; }
                    FsmHooks.Insert(st, at, new Hook { B = b, State = s, Box = true, After = true });
                }
                else FsmHooks.Prepend(st, new Hook { B = b, State = s, Box = true });
                n++;
            }
            b.BoxHooked = true;
            Log.Info("usine : caisse " + b.BoxGo.name + " accrochee (" + n + " etats rejoues)");
            return true;
        }

        static GameObject Created(PlayMakerFSM f, string state)
        {
            FsmState st = f.Fsm.GetState(state);
            if (st == null || st.Actions == null) return null;
            foreach (FsmStateAction a in st.Actions)
            {
                var c = a as CreateObject;
                if (c != null && c.storeObject != null && c.storeObject.Value != null) return c.storeObject.Value;
            }
            return null;
        }

        static void OnLocal(Hook h)
        {
            var w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@usine");
            if (!h.Box)
            {
                Session.SendAll(w.U8(1).U8(h.B.Index).Str("").Str(""), true);
                Log.Info("usine : caisse de " + Bins[h.B.Index] + " sortie ici, envoye");
                return;
            }
            string key = "";
            if (h.State == "Pick item")
            {
                GameObject go = Created(h.B.Box, h.State);
                if (go != null) { key = "usine:" + Session.LocalId + ":" + (++made); Props.Adopt(go, key); }
            }
            Session.SendAll(w.U8(2).U8(h.B.Index).Str(h.State).Str(key), true);
            Log.Info("usine : " + Bins[h.B.Index] + ", caisse -> " + h.State + " ici" + (key.Length > 0 ? " (piece " + key + ")" : "") + ", envoye");
        }

        static void OnReplayed(Hook h)
        {
            if (pendingKey == null || h.State != "Pick item") return;
            GameObject go = Created(h.B.Box, h.State);
            if (go != null) Props.Adopt(go, pendingKey);
            Log.Info("usine : piece " + pendingKey + (go != null ? " creee ici (" + go.name + ")" : " : rien de cree ici"));
            pendingKey = null;
        }

        public static void OnMessage(int who, NetReader r)
        {
            int kind = r.U8(), bi = r.U8();
            string state = r.Str(), key = r.Str();
            if (!On || bi >= bins.Length) return;
            Bin b = bins[bi];
            if (b == null || !b.Hooked) { TryBin(bi); b = bins[bi]; }
            if (b == null || !b.Hooked) { Log.Warn("usine : bac " + (bi < Bins.Length ? Bins[bi] : "?") + " pas encore trouve ici (#" + who + ")"); return; }
            if (kind == 1)
            {
                if (!b.BoxGo.activeSelf) b.BoxGo.SetActive(true);
                if (!b.BoxHooked) HookBox(b);
                Props.HoldFromRemote(b.BoxGo, who);
                Log.Info("usine : caisse de " + Bins[bi] + " sortie par #" + who + ", en main chez lui");
                return;
            }
            if (!b.BoxGo.activeSelf) b.BoxGo.SetActive(true);
            if (!b.BoxHooked) HookBox(b);
            if (b.Box == null || b.Box.Fsm.GetState(state) == null) return;
            pendingKey = key.Length > 0 ? key : null;
            Replay.Depth++;
            try { Game.SetState(b.Box, state); }
            finally { Replay.Depth--; pendingKey = null; }
            Log.Info("usine : " + Bins[bi] + ", caisse -> " + state + " par #" + who + " (etat " + b.Box.ActiveStateName + ")");
        }

        // [Test] Autotest=usinetravail (2 joueurs, TestPos dans l'usine : 2172,0.5,-470) : 40 s apres l'arrivee de l'autre, le joueur
        // [Test] UsineActeur (invite par defaut) sort la caisse de chargeurs (Pick BOX), l'ouvre a +4 s (Check open), prend une
        // piece a +8 s et +12 s ; chacun note toutes les 4 s la caisse (allumee, position, Items) et les pieces usine:*.
        static int tStep; static float tAt, tLog;
        public static void Test(string mode, float t)
        {
            if (mode != "usinetravail") return;
            float now = Time.realtimeSinceStartup;
            if (tStep == 0 && Jobs.OtherInGame(30f)) { tStep = 1; tAt = now; }
            if (tStep == 0) return;
            bool actor = Session.IsHost == (Config.Get("Test", "UsineActeur", "invite") == "hote");
            Bin b = bins[0];
            if (actor && b != null && b.Hooked)
            {
                if (tStep == 1) { tStep = 2; Game.SetState(b.Use, "Pick BOX"); Log.Info("autotest : usine, Pick BOX -> " + b.Use.ActiveStateName + ", caisse " + (b.BoxGo.activeSelf ? "allumee" : "eteinte")); }
                if (tStep == 2 && now - tAt > 4f && b.Box != null) { tStep = 3; Game.SetState(b.Box, "Check open"); Log.Info("autotest : usine, ouverture -> " + b.Box.ActiveStateName); }
                if ((tStep == 3 && now - tAt > 8f) || (tStep == 4 && now - tAt > 12f)) { tStep++; Game.SetState(b.Box, "Check open"); Log.Info("autotest : usine, prise d'une piece -> " + b.Box.ActiveStateName); }
                // (+16 s : la premiere piece en main, promenee 6 s ; +24 s : lachee)
                if (tStep == 5 && now - tAt > 16f) { tStep = 6; Log.Info("autotest : usine, piece en main : " + (Props.TestHold("usine:" + Session.LocalId + ":1", PlayerSync.LocalCamera) != null)); }
                if (tStep == 6 && now - tAt > 24f) { tStep = 7; Props.TestHold("usine:" + Session.LocalId + ":1", null); Log.Info("autotest : usine, piece lachee"); }
            }
            if (now >= tLog && now - tAt < 40f)
            {
                tLog = now + 4f;
                string box = b != null && b.BoxGo != null ? b.BoxGo.name + (b.BoxGo.activeInHierarchy ? " allumee" : " eteinte") + " en " + b.BoxGo.transform.position.ToString("F1")
                             + (b.Box != null ? ", Items " + (b.Box.FsmVariables.FindFsmInt("Items") != null ? b.Box.FsmVariables.FindFsmInt("Items").Value.ToString() : "?") + ", Open " + (b.Box.FsmVariables.FindFsmBool("Open") != null ? b.Box.FsmVariables.FindFsmBool("Open").Value.ToString() : "?") + " [" + b.Box.ActiveStateName + "]" : "") : "pas de bac";
                Rigidbody p1 = null;
                foreach (PlayerInfo pi in Session.Players.Values) { Rigidbody rb = Props.BodyOf("usine:" + pi.Id + ":1"); if (rb != null) p1 = rb; }
                Log.Info("autotest : usine t=" + (now - tAt).ToString("F0") + " : caisse " + box + " ; tenue par #" + (b != null && b.BoxGo != null ? Props.RemoteHolder(b.BoxGo).ToString() : "?")
                         + " ; piece 1 " + (p1 != null ? "en " + p1.position.ToString("F2") + (p1.isKinematic ? " (cin)" : "") + ", tenue par #" + Props.RemoteHolder(p1.gameObject) : "absente"));
            }
        }
    }
}
