using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Vitrines brisees du pub et du magasin (STORE_AREA/.../BreakableWindowsPub...). La vitre est un corps tenu par un
    // FixedJoint ; un coup ou un choc casse le joint, son automate 'Break' (State 1 : HasComponent FixedJoint) passe en
    // 'Shatter' : vitre intacte cachee, eclats (store_windows_broken : corps libres) allumes, WindowPubBroken ; puis 'Check
    // Teimo' -> ... -> 'Cash window' (2760 mk sur la note de CE joueur chez Teimo) -> 'Destroy' (tout le groupe, ~15-20 s).
    // Rien de tout ca n'etait partage : chez l'autre la vitre restait entiere et les eclats eteints -- un eclat tenu n'y
    // apparaissait pas, lache il glissait de son cote (liste de Menor, 18/10). Ici : celui chez qui la vitre casse envoie
    // @vitrine ; les autres cassent le joint, passent l'automate en 'Shatter' (memes eclats, memes cles Props sous
    // STORE_AREA) et le figent juste apres (pas de note chez eux : seul celui qui a casse paie), puis 'Destroy' 20 s plus tard.
    // Audit MSC 02/11 :
    //  - My Summer Car : memes automates sous STORE/LOD/GFX_Store/BreakableWindows et STORE/LOD/GFX_Pub/BreakableWindowsPub
    //    (releve generique, rien a changer) ; le module ne tournait pas sur MSC (Core.MscModules) : chaque joueur proche payait.
    //  - Le 'Use' de la vitre (coup de poing : 'Mouse over 2' -HIT-> 'Break', DestroyComponent FixedJoint) etait rejoue par le
    //    monde (WorldFsms, releve a 16 s) : chez l'autre le joint sautait, son propre 'Break' deroulait tout (2760 mk sur SA
    //    note, Teimo fache contre lui) et il la croyait cassee chez lui. Ce module le prend maintenant avant le monde (releve a
    //    12 s, Replay.Claim) : la casse n'arrive aux autres que par @vitrine. [Test] VitrineUse=0 : comme avant (releve a 20 s,
    //    'Use' laisse au monde). [Test] VitrinesMsc=0 : module eteint sur MSC, comme avant.
    public static class Vitrines
    {
        class W { public PlayMakerFSM F; public string Key; public bool Done, Remote; public float FreezeAt = -1, DestroyAt = -1; }
        static readonly List<W> windows = new List<W>();
        static float findAt = -1, nextPoll;

        public static void OnLevelLoaded()
        {
            windows.Clear(); claimed = 0;
            claimUse = Config.GetInt("Test", "VitrineUse", 1) != 0;
            bool on = !Game.Msc || Config.GetInt("Test", "VitrinesMsc", 1) != 0;
            findAt = PlayerSync.InGame && on ? Time.realtimeSinceStartup + (claimUse ? 12f : 20f) : -1;   // (12 s : avant le monde, 16 s)
        }

        static bool claimUse;
        static int claimed;

        static void Find()
        {
            foreach (Object o in Game.FsmsNamed("Break"))
            {
                var f = o as PlayMakerFSM;
                if (f == null || f.hideFlags != HideFlags.None || f.FsmName != "Break" || !f.gameObject.name.StartsWith("BreakableWindow")) continue;
                if (f.Fsm.GetState("Shatter") == null || f.Fsm.GetState("Destroy") == null) continue;
                string key = Recon.Path(f.transform) + "#" + f.transform.GetSiblingIndex();
                bool known = false;
                foreach (W w in windows) if (w.F == f) known = true;
                if (known) continue;
                windows.Add(new W { F = f, Key = key, Done = f.ActiveStateName != "State 1" });
                // Coup de poing ('Use' de la meme vitre) : pris ici, le monde ne le rejoue plus (voir plus haut).
                PlayMakerFSM use = claimUse ? Game.FsmOn(f.gameObject, "Use") : null;
                if (use != null && Replay.Claim(use, "vitrines")) claimed++;
                else if (use != null) Log.Warn("vitrines : 'Use' de " + key + " deja pris par " + Replay.Owner(use) + " : son coup de poing reste rejoue");
            }
            if (windows.Count > 0) Log.Info("vitrines : " + windows.Count + " vitre(s) cassable(s) suivie(s)" + (claimUse ? ", " + claimed + " 'Use' (coup de poing) a ce module" : ""));
        }

        public static void Update()
        {
            if (!PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if (findAt > 0 && now >= findAt) { findAt = -1; Find(); if (windows.Count == 0) findAt = now + 30f; }
            if (now < nextPoll) return;
            nextPoll = now + 0.2f;
            foreach (W w in windows)
            {
                if (w.F == null) continue;
                if (w.FreezeAt > 0 && now >= w.FreezeAt) { w.FreezeAt = -1; w.F.enabled = false; }   // (avant 'Check Teimo' : pas de note ici)
                if (w.DestroyAt > 0 && now >= w.DestroyAt)
                {
                    w.DestroyAt = -1; w.F.enabled = true;
                    Game.SetState(w.F, "Destroy");
                    Log.Info("vitrines : " + w.Key + " retiree (comme chez celui qui l'a cassee)");
                    continue;
                }
                if (w.Done || w.F.ActiveStateName == "State 1") continue;
                // Cassee ici (coup, choc, objet lance) : les autres la cassent aussi.
                w.Done = true;
                if (Session.Active && Session.RemoteCount > 0)
                    Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@vitrine").Str(w.Key), true);
                Log.Info("vitrines : " + w.Key + " cassee ici (" + w.F.ActiveStateName + ")");
            }
        }

        public static void OnMessage(int who, NetReader r)
        {
            string key = r.Str();
            W w = null;
            foreach (W x in windows) if (x.Key == key && x.F != null) { w = x; break; }
            if (w == null) { Log.Warn("vitrines : " + key + " introuvable ici"); return; }
            if (w.Done) { Log.Info("vitrines : " + key + " deja cassee ici"); return; }
            w.Done = true; w.Remote = true;
            FixedJoint fj = w.F.GetComponent<FixedJoint>();
            if (fj != null) Object.Destroy(fj);
            w.F.enabled = true;
            Game.SetState(w.F, "Shatter");
            w.FreezeAt = Time.realtimeSinceStartup + 0.5f;   // ('Shatter' attend 1,7 s avant 'Check Teimo')
            w.DestroyAt = Time.realtimeSinceStartup + 20f;
            Log.Info("vitrines : " + key + " cassee par #" + who + " (eclats ici, pas de note)");
        }

        public static string State()
        {
            var sb = new System.Text.StringBuilder("vitrines :");
            foreach (W w in windows) sb.Append(' ').Append(w.Key).Append(" [").Append(w.F != null ? w.F.ActiveStateName + (w.F.enabled ? "" : ", figee") : "detruite").Append(w.Done ? ", cassee" : "").Append(']');
            // Note de la caisse de Teimo (ce que 'Cash window' ajoute ici) et a qui est le coup de poing.
            GameObject reg = Game.FindAny(Game.Msc ? "STORE/StoreCashRegister/Register" : "PERAPORTTI/ActiveFunctions/Store/Cashier/StoreCashRegister/CashRegisterLogic");
            PlayMakerFSM data = reg != null ? Game.FsmOn(reg, "Data") : null;
            FsmFloat bw = data != null ? data.FsmVariables.FindFsmFloat("BrokenWindow") : null;
            sb.Append(" ; BrokenWindow ici ").Append(bw != null ? bw.Value.ToString("F0") : "-");
            PlayMakerFSM use = windows.Count > 0 && windows[0].F != null ? Game.FsmOn(windows[0].F.gameObject, "Use") : null;
            sb.Append(", 'Use' de la 1re a ").Append(use != null ? Replay.Owner(use) ?? "personne" : "-");
            return sb.ToString();
        }

        // [Test] Autotest=vitrine : l'hote casse la premiere vitre a 40 s (joint retire, comme un coup) ; chacun note toutes les 3 s.
        // [Test] VitrinePoing=1 (audit MSC 02/11) : c'est l'INVITE qui casse a 40 s, par le coup de poing ('Use' -> 'Break', comme
        // le jeu). Attendu chez l'hote : "cassee par #1 (eclats ici, pas de note)", pas de ligne "monde de #1 ...BreakableWindow
        // ...::Use", BrokenWindow ici 0 ; chez l'invite "cassee ici (Shatter)" puis BrokenWindow 2760 (s'il est a moins de 7 m,
        // magasin ouvert). Sans VitrinePoing : l'hote casse, BrokenWindow 0 chez l'invite.
        static int testStep; static float testLog;
        public static void Test(string mode, float t)
        {
            if (mode != "vitrine") return;
            bool punch = Config.GetInt("Test", "VitrinePoing", 0) != 0;
            if (Session.IsHost != punch && t > 40f && testStep == 0 && windows.Count > 0 && windows[0].F != null)
            {
                testStep = 1;
                PlayMakerFSM use = punch ? Game.FsmOn(windows[0].F.gameObject, "Use") : null;
                if (use != null) { Game.SetState(use, "Break"); Log.Info("autotest : vitrine, coup de poing de l'invite sur " + windows[0].Key + " ('Use' -> " + use.ActiveStateName + ")"); }
                else
                {
                    FixedJoint fj = windows[0].F.GetComponent<FixedJoint>();
                    if (fj != null) Object.Destroy(fj);
                    Log.Info("autotest : vitrine, joint de " + windows[0].Key + " retire");
                }
            }
            if (t > 30f && t < 75f && t - testLog >= 3f) { testLog = t; Log.Info("autotest : " + State()); }
        }
    }
}
