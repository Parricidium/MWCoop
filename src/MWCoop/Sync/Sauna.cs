using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Vapeur du sauna chez les autres joueurs (retour du 14/10, « la vapeur du sauna n'apparait pas pour l'autre joueur »).
    // L'eau jetee a la louche touche le declencheur du poele (StoveTrigger :: Steam) : au-dela de 350 degres, etat "Steam" --
    // son sauna_steam, vapeur (emetteur Steam du poele), flou d'ecran et stress en moins pour celui qui a jete l'eau.
    // Ici : a l'entree de "Steam" chez un joueur, @vapeur (poele, duree) ; les autres allument la vapeur de ce poele le meme
    // temps et entendent le son, SANS le flou ni le stress (effets personnels). Deux poeles : maison (YARD) et chalet (COTTAGE).
    public static class Sauna
    {
        static readonly string[] Stoves = { "YARD/Building/SAUNA/Sauna/Kiuas", "COTTAGE/Stuff/Sauna/Stove" };
        class Stove { public PlayMakerFSM F; public ParticleEmitter Steam; public bool Hooked; public float OffAt = -1f; public int Peak; }
        static readonly Stove[] stoves = new Stove[2];
        static float nextLook;
        static int logs;

        public static void OnLevelLoaded() { for (int i = 0; i < stoves.Length; i++) stoves[i] = null; nextLook = 0f; }

        class SteamHook : ModHook
        {
            public override string Module { get { return "sauna"; } }
            public int Index;
            public override void OnEnter()
            {
                try { if (Replay.Depth == 0) Local(Index); } catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        static float Duration(Stove s)
        {
            FsmFloat d = s.F != null ? s.F.FsmVariables.FindFsmFloat("SteamTime") : null;
            return d != null && d.Value > 0.5f && d.Value < 60f ? d.Value : 6f;
        }

        static void Local(int i)
        {
            Stove s = stoves[i];
            if (s == null || !Session.Active || Session.RemoteCount == 0) return;
            float dur = Duration(s);
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@vapeur").U8(i).F32(dur), true);
            if (logs++ < 10) Log.Info("sauna : vapeur ici (poele " + i + ", " + dur.ToString("F1") + " s), envoyee");
        }

        public static void OnRemote(int who, NetReader r)
        {
            int i = r.U8(); float dur = r.F32();
            if (Session.LocalId == who || i >= stoves.Length) return;
            Find();
            Stove s = stoves[i];
            if (s == null || s.Steam == null) return;
            if (float.IsNaN(dur) || dur < 0.5f || dur > 60f) dur = 6f;
            s.Steam.emit = true;
            s.OffAt = Time.realtimeSinceStartup + dur; s.Peak = 0;
            try { MasterAudio.PlaySound3DAtVector3AndForget("HouseFoley", s.Steam.transform.position, 1f, null, 0f, "sauna_steam"); } catch { }
            if (logs++ < 10) Log.Info("sauna : vapeur de #" + who + " (poele " + i + ", " + dur.ToString("F1") + " s)");
        }

        static void Find()
        {
            for (int i = 0; i < Stoves.Length; i++)
            {
                if (stoves[i] != null && stoves[i].F != null) continue;
                GameObject g = Game.FindAny(Stoves[i] + "/StoveTrigger");
                PlayMakerFSM f = g != null ? Game.FsmOn(g, "Steam") : null;
                if (f == null) continue;
                Transform st = g.transform.parent != null ? g.transform.parent.Find("Steam") : null;
                stoves[i] = new Stove { F = f, Steam = st != null ? st.GetComponent<ParticleEmitter>() : null };
            }
        }

        public static void Update()
        {
            if (!Session.Active || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextLook)
            {
                nextLook = now + 5f;
                Find();
                for (int i = 0; i < stoves.Length; i++)
                {
                    Stove s = stoves[i];
                    if (s == null || s.Hooked || s.F == null) continue;
                    FsmState st = s.F.Fsm.GetState("Steam");
                    if (st == null || !st.IsInitialized) continue;   // (automate jamais demarre : au prochain releve)
                    FsmHooks.Prepend(st, new SteamHook { Index = i });
                    s.Hooked = true;
                    Log.Info("sauna : vapeur du poele " + i + " suivie (" + Stoves[i] + ")");
                }
            }
            // vapeur d'un autre : eteinte a la fin (sauf si notre propre automate l'a reprise)
            for (int i = 0; i < stoves.Length; i++)
            {
                Stove s = stoves[i];
                if (s == null || s.OffAt < 0f) continue;
                if (s.Steam != null && s.Steam.particleCount > s.Peak) s.Peak = s.Steam.particleCount;
                if (now < s.OffAt) continue;
                s.OffAt = -1f;
                if (logs++ < 10) Log.Info("sauna : vapeur de l'autre finie (poele " + i + ", jusqu'a " + s.Peak + " particules)");
                if (s.Steam != null && (s.F == null || s.F.ActiveStateName != "Steam")) s.Steam.emit = false;
            }
        }

        // Essai Autotest=vapeur : l'hote fait de la vapeur au poele de la maison a 40 s ; chacun note l'emetteur a 41 et 50 s.
        static int tStep;
        public static void Test(string mode, float t)
        {
            if (mode != "vapeur") return;
            Find();
            Stove s = stoves[0];
            if (s == null) return;
            if (Session.IsHost && t > 40f && tStep == 0) { tStep = 1; Game.SetState(s.F, "Steam"); Log.Info("autotest : vapeur, Steam force ici"); }
            if (t > 41.5f && tStep <= 1) { tStep = 2; Log.Info("autotest : vapeur a 41 s : emet " + (s.Steam != null && s.Steam.emit) + ", particules " + (s.Steam != null ? s.Steam.particleCount : -1) + ", etat " + s.F.ActiveStateName); }
            if (t > 55f && tStep == 2) { tStep = 3; Log.Info("autotest : vapeur a 55 s : emet " + (s.Steam != null && s.Steam.emit) + ", particules " + (s.Steam != null ? s.Steam.particleCount : -1) + ", etat " + s.F.ActiveStateName); }
        }
    }
}
