using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Mecanique de la SATSUMA (My Summer Car ; passe Satsuma du 27/10, demande de JD : « chaque etat, chaque variable les memes pour
    // tout le monde »). Trois trous de l'audit :
    //  - les automates de casse du moteur et des trains (CarSimulation/Car/Redlining::RedLining, Engine/Oil::Oil, Symptoms/SuspDamage/
    //    Calculations::FL..RR, RandomBolt::Logic, Symptoms/GearboxDamage::Damage, Engine/Symptoms::Stuck Throttle) tournent chez chacun,
    //    sur la copie aussi (regime recu dans le Drivetrain, physique des roues de la voiture garee) : chacun tirait ses casses au sort --
    //    coussinets detruits chez l'invite et pas chez l'hote, usure comptee deux fois. Ici ils ne tournent que chez celui qui fait
    //    autorite sur la simulation (VehicleSync.SimAuthority : le conducteur, sinon l'hote) ; ailleurs ils sont coupes (PlayMakerFSM
    //    enabled, 2 s d'hysteresis ; PlayMaker les reprend a leur etat de depart au retour : repos) ;
    //  - Database/*/Data.Damaged (104 pieces : culasse, vilebrequin, boite, struts...), pose par IS_DAMAGED (RedLining, Oil, SuspDamage,
    //    Cylinders) et leve par REPAIR : jamais envoye -- moteur mort chez un seul. Celui qui fait autorite envoie l'entree de l'etat
    //    atteint par IS_DAMAGED ou REPAIR (@abime) ; chez les autres le meme evenement est envoye a l'entree (Damaged, maillage
    //    « Damaged » allume ou eteint, comme le jeu). L'hote redit toutes les 10 s les bits de toutes les entrees (@abimes) : un ecart
    //    est corrige par le meme evenement, sauf sur une entree qui vient de changer ici (5 s) ;
    //  - usure centrale (CarSimulation/MechanicalWear::Data) : les flottants WearX et CarbChamberLevel vont par Fluids (WearSim),
    //    l'entier GearboxDamageType avec les bits.
    // Les detachements de pieces qui suivent une casse (BoltCheck 'Break off' -> Removal) passent par Parts, comme avant, avec le
    // 'Break off' rejoue chez les autres (PartsMsc.MscOnMessage).
    // [Test] MecaGarde=0 : automates de casse laisses tourner partout (comme avant) ; MecaAbime=0 : Damaged pas suivi.
    public static class MecaMsc
    {
        class Entry { public string Key; public uint Hash; public PlayMakerFSM F; public FsmBool Damaged; public bool Last; public float LastChange; }
        static void Send(Entry e, bool damaged, string how)
        {
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@abime").Str(e.Key).U8(damaged ? (byte)1 : (byte)0), true);
            Log.Info("mecanique (MSC) : " + e.Key + (damaged ? " abimee" : " reparee") + " ici (" + how + "), envoye");
        }
        class Hook : ModHook
        {
            public override string Module { get { return "mecanique (MSC)"; } }
            public Entry E; public bool Damaged;
            public override void OnEnter()
            {
                try
                {
                    // (le jeu repasse par ces etats sans changer la valeur -- REPAIR diffuse au chargement sur 9 entrees saines, 27/10 : rien a dire)
                    if (E.Damaged.Value != Damaged)
                    {
                        E.LastChange = Time.realtimeSinceStartup;
                        E.Last = Damaged;
                        if (!applying && Replay.Depth == 0 && Session.Active && Authority()) Send(E, Damaged, "crochet");
                    }
                }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        static readonly List<Entry> entries = new List<Entry>();
        static readonly Dictionary<string, Entry> byKey = new Dictionary<string, Entry>();
        static readonly Dictionary<uint, Entry> byHash = new Dictionary<uint, Entry>();
        static bool applying;
        static float nextScan = -1f, nextGate, nextDigest, loadedAt;
        // Automates de casse, sous la racine de la SATSUMA : chemin, automate.
        static readonly string[][] Gated = {
            new[] { "CarSimulation/Car/Redlining", "RedLining" },
            new[] { "CarSimulation/Engine/Oil", "Oil" },
            new[] { "CarSimulation/Symptoms/SuspDamage/Calculations", "FL" }, new[] { "CarSimulation/Symptoms/SuspDamage/Calculations", "FR" },
            new[] { "CarSimulation/Symptoms/SuspDamage/Calculations", "RL" }, new[] { "CarSimulation/Symptoms/SuspDamage/Calculations", "RR" },
            new[] { "CarSimulation/RandomBolt", "Logic" },
            new[] { "CarSimulation/Symptoms/GearboxDamage", "Damage" },
            new[] { "CarSimulation/Engine/Symptoms", "Stuck Throttle" },
        };
        static readonly List<PlayMakerFSM> gated = new List<PlayMakerFSM>();
        static bool gatedOn = true, lastAuth = true; static float authSince;
        static PlayMakerFSM wear;   // CarSimulation/MechanicalWear::Data

        public static void OnLevelLoaded()
        {
            entries.Clear(); byKey.Clear(); byHash.Clear(); gated.Clear(); wear = null;
            gatedOn = true; lastAuth = true; authSince = 0f;
            loadedAt = Time.realtimeSinceStartup;
            nextScan = Game.Msc && PlayerSync.InGame ? loadedAt + 12f : -1f;
            nextGate = nextDigest = 0f;
        }

        static Rigidbody Satsuma() { return VehicleSync.Body("SATSUMA"); }
        static bool Authority()
        {
            Rigidbody sat = Satsuma();
            return sat != null ? VehicleSync.SimAuthority(sat.transform) : Session.IsHost;
        }

        static uint H(string s)
        {
            uint h = 2166136261u;
            foreach (char c in s) { h ^= c; h *= 16777619u; }
            return h;
        }

        static void Scan()
        {
            GameObject db = Game.FindAny("Database");
            int n = 0, hooks = 0;
            if (db != null && Config.GetInt("Test", "MecaAbime", 1) != 0)
                foreach (Transform cat in db.transform)
                    foreach (Transform e in cat)
                    {
                        string key = cat.name + "/" + e.name;
                        if (byKey.ContainsKey(key)) continue;
                        PlayMakerFSM d = Game.FsmOn(e.gameObject, "Data");
                        FsmBool dmg = d != null ? d.FsmVariables.FindFsmBool("Damaged") : null;
                        if (dmg == null || d.Fsm == null) continue;
                        // Toutes les entrees a Damaged (104) ; crochets la ou IS_DAMAGED / REPAIR existent (15 : moteur), le relevé 1/s pour les autres
                        // (Damaged pose par la piece elle-meme : ailes, struts, pare-chocs...).
                        string sDmg = null, sRep = null;
                        foreach (FsmTransition tr in d.FsmGlobalTransitions) { if (tr.EventName == "IS_DAMAGED") sDmg = tr.ToState; else if (tr.EventName == "REPAIR") sRep = tr.ToState; }
                        var en = new Entry { Key = key, Hash = H(key), F = d, Damaged = dmg, Last = dmg.Value };
                        if (sDmg != null && FireTools.Insert(d, d.Fsm.GetState(sDmg), new Hook { E = en, Damaged = true }, false)) hooks++;
                        if (sRep != null && FireTools.Insert(d, d.Fsm.GetState(sRep), new Hook { E = en, Damaged = false }, false)) hooks++;
                        entries.Add(en); byKey[key] = en; byHash[en.Hash] = en;
                        n++;
                    }
            Rigidbody sat = Satsuma();
            if (sat != null && gated.Count == 0)
            {
                foreach (string[] g in Gated)
                {
                    Transform t = sat.transform.Find(g[0]);
                    PlayMakerFSM f = t != null ? Game.FsmOn(t.gameObject, g[1]) : null;
                    if (f != null) gated.Add(f);
                }
                Transform w = sat.transform.Find("CarSimulation/MechanicalWear");
                wear = w != null ? Game.FsmOn(w.gameObject, "Data") : null;
            }
            if (n > 0) Log.Info("mecanique (MSC) : " + entries.Count + " entrees Database (Damaged) suivies (" + hooks + " crochets), " + gated.Count + " automates de casse sous la main" + (wear != null ? ", usure centrale" : ""));
        }

        // Releve 1/s : une entree dont Damaged a change ici sans passer par un crochet (ou crochet muet) part de meme, par l'autorite.
        static float nextPoll;
        static void Poll(float now)
        {
            if (!Session.Active || Session.RemoteCount == 0) return;
            bool auth = Authority();
            foreach (Entry e in entries)
            {
                if (e.F == null || e.Damaged.Value == e.Last) continue;
                e.Last = e.Damaged.Value; e.LastChange = now;
                if (auth) Send(e, e.Last, "releve");
            }
        }

        public static void Update()
        {
            if (!Game.Msc || nextScan < 0f || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextScan) { nextScan = now + 30f; Scan(); }
            if (now >= nextGate) { nextGate = now + 0.5f; Gate(now); }
            if (now >= nextPoll) { nextPoll = now + 1f; Poll(now); }
            if (Session.IsHost && Session.Active && Session.RemoteCount > 0 && now >= nextDigest && now - loadedAt > 30f) { nextDigest = now + 10f; Digest(); }
        }

        // Automates de casse : allumes chez celui qui fait autorite sur la SATSUMA, coupes ailleurs (hors session : partout).
        static void Gate(float now)
        {
            if (gated.Count == 0) return;
            bool auth = !Session.Active || Session.RemoteCount == 0 || Config.GetInt("Test", "MecaGarde", 1) == 0 || Authority();
            if (auth != lastAuth) { lastAuth = auth; authSince = now; }
            if (auth == gatedOn || now - authSince < 2f) return;
            gatedOn = auth;
            int n = 0;
            foreach (PlayMakerFSM f in gated) if (f != null) { f.enabled = auth; n++; }
            Log.Info("mecanique (MSC) : " + n + " automates de casse " + (auth ? "repris ici (autorite sur la SATSUMA)" : "coupes ici (un autre fait autorite)"));
        }

        static void Digest()
        {
            if (entries.Count == 0) return;
            var w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@abimes");
            foreach (Entry e in entries) { if (e.F == null) continue; w.I32((int)e.Hash).U8(e.Damaged.Value ? (byte)1 : (byte)0); }
            FsmInt gt = wear != null ? wear.FsmVariables.FindFsmInt("GearboxDamageType") : null;
            if (gt != null) w.I32((int)H("GearboxDamageType")).U8((byte)Mathf.Clamp(gt.Value, 0, 255));
            Session.SendAll(w, true);
        }

        static void Apply(Entry e, bool damaged, int who, string how)
        {
            if (e.F == null || e.Damaged.Value == damaged) return;
            applying = true; Replay.Depth++;
            try { e.F.SendEvent(damaged ? "IS_DAMAGED" : "REPAIR"); }
            finally { applying = false; Replay.Depth--; }
            if (e.Damaged.Value != damaged) e.Damaged.Value = damaged;   // (pas de transition IS_DAMAGED / REPAIR : la valeur au moins)
            e.Last = damaged; e.LastChange = Time.realtimeSinceStartup;
            Log.Info("mecanique (MSC) : " + e.Key + (damaged ? " abimee" : " reparee") + " (joueur #" + who + ", " + how + ")");
        }

        public static void OnMessage(int who, NetReader r)
        {
            string key = r.Str(); bool damaged = r.U8() != 0;
            Entry e;
            if (!PlayerSync.InGame || nextScan < 0f) return;   // (pas encore en jeu : la sauvegarde recue a la valeur)
            if (!byKey.TryGetValue(key, out e)) { Scan(); byKey.TryGetValue(key, out e); }
            if (e == null) { Log.Warn("mecanique (MSC) : " + key + " inconnue ici"); return; }
            Apply(e, damaged, who, "a chaud");
        }

        public static void OnDigest(int who, NetReader r)
        {
            if (Session.IsHost) return;
            float now = Time.realtimeSinceStartup;
            uint gtH = H("GearboxDamageType");
            int fixedN = 0;
            while (r.More)
            {
                uint h = (uint)r.I32(); int val = r.U8(); bool bit = val != 0;
                if (h == gtH)
                {
                    FsmInt gt = wear != null ? wear.FsmVariables.FindFsmInt("GearboxDamageType") : null;   // (valeur, pas un bit)
                    if (gt != null && gt.Value != val && !Authority()) { gt.Value = val; fixedN++; }
                    continue;
                }
                Entry e;
                if (!byHash.TryGetValue(h, out e) || e.F == null || now - e.LastChange < 5f) continue;
                if (e.Damaged.Value != bit) { Apply(e, bit, who, "controle de l'hote"); fixedN++; }
            }
            if (fixedN > 0) Log.Info("mecanique (MSC) : controle de l'hote : " + fixedN + " entree(s) corrigee(s)");
        }

        // Essais (Autotest=mscmoteur, chez l'hote) : une casse comme RedLining la fait -- IS_DAMAGED au vilebrequin (Database) et au
        // coussinet 1 (BoltCheck : 'Break off', piece detachee), usure du piston 1 posee a 77,7 (MechanicalWear, par Fluids).
        public static string TestDamage()
        {
            var sb = new System.Text.StringBuilder();
            if (wear != null) { FsmFloat p1 = wear.FsmVariables.FindFsmFloat("WearPiston1"); if (p1 != null) { p1.Value = 77.7f; sb.Append("WearPiston1=77.7 ; "); } }
            else sb.Append("MechanicalWear introuvable ; ");
            Entry e;
            if (byKey.TryGetValue("DatabaseMotor/Crankshaft", out e)) { e.F.SendEvent("IS_DAMAGED"); sb.Append("Crankshaft IS_DAMAGED -> Damaged=" + e.Damaged.Value + " ; "); }
            else sb.Append("Crankshaft non suivi ; ");
            Entry cb1;
            FsmGameObject tp = byKey.TryGetValue("DatabaseMotor/CrankBearing1", out cb1) ? cb1.F.FsmVariables.FindFsmGameObject("ThisPart") : null;
            GameObject mb = tp != null ? tp.Value : null;
            PlayMakerFSM bc = mb != null ? Game.FsmOn(mb, "BoltCheck") : null;
            if (bc != null && mb.activeInHierarchy) { bc.SendEvent("IS_DAMAGED"); sb.Append("main bearing1 IS_DAMAGED -> [" + bc.ActiveStateName + "]"); }
            else sb.Append("main bearing1 " + (mb == null ? "introuvable" : "inactif ou sans BoltCheck"));
            return sb.ToString();
        }
    }
}
