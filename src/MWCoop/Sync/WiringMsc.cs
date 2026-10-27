using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Fils electriques de la SATSUMA (My Summer Car ; retour de Reinmansed, 19/10 : « l'invite branche les fils, l'hote ne voit
    // rien »). Le joueur tient le « wiring mess » (outil) et clique une borne : SATSUMA/Wiring/Triggers/<fil>/<borne>, automate
    // 'Assemble' -- 'State 1' (outil a moins de 10 cm) -> 'Assemble' (clic) -> 'Sound' (CLOSELOOP a l'autre bout) -> 'Finish
    // assembly' : Database/DatabaseWiring/<fil> Installed, fil (Wiring/Parts/wiring_xxx(xxxxx)) allume, cosse a boulonner. Les
    // bornes sont des objets eteints, allumes par Wiring::Status d'apres les pieces montees. Rien de tout ca ne passait.
    // Ici : « Finish assembly » atteint par le joueur est envoye (cle = chemin de la borne sous Triggers) ; chez les autres, la
    // borne est allumee le temps de poser cet etat (le fil, la base et la cosse suivent), puis eteinte si elle l'etait.
    public static class WiringMsc
    {
        class Hook : ModHook
        {
            public override string Module { get { return "fils"; } }
            public string Key;
            public override void OnEnter()
            {
                try
                {
                    if (!applying && Replay.Depth == 0 && Session.Active)
                    {
                        Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@fils").Str(Key), true);
                        MWCoop.Log.Info("fils : " + Key + " branche ici, envoye");
                    }
                }
                catch (System.Exception e) { MWCoop.Log.Warn("fils : " + e.Message); }
                Finish();
            }
        }

        static readonly Dictionary<string, PlayMakerFSM> bornes = new Dictionary<string, PlayMakerFSM>();
        static readonly List<KeyValuePair<GameObject, int>> relight = new List<KeyValuePair<GameObject, int>>();   // (borne allumee pour le rejeu : eteinte a cette image)
        static bool applying;
        static float nextScan = -1f;

        public static void OnLevelLoaded() { bornes.Clear(); relight.Clear(); nextScan = Game.Msc && PlayerSync.InGame ? Time.realtimeSinceStartup + 15f : -1f; }

        static void Scan()
        {
            GameObject sat = null;
            foreach (Rigidbody rb in Object.FindObjectsOfType<Rigidbody>()) if (rb.transform.parent == null && rb.name.StartsWith("SATSUMA")) { sat = rb.gameObject; break; }
            Transform trig = sat != null ? sat.transform.Find("Wiring/Triggers") : null;
            if (trig == null) { if (bornes.Count == 0) Log.Info("fils : SATSUMA/Wiring/Triggers introuvable"); return; }
            int n = 0;
            foreach (PlayMakerFSM f in trig.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                if (f.FsmName != "Assemble" || bornes.ContainsValue(f)) continue;
                string key = Recon.Path(f.transform);
                int i = key.IndexOf("/Triggers/");
                if (i < 0) continue;
                key = key.Substring(i + 10);
                FsmState st = null;
                try { if (f.Fsm != null) { if (!f.Fsm.Initialized) f.Fsm.InitData(); st = f.Fsm.GetState("Finish assembly"); } } catch { }
                if (st == null) continue;
                if (!FireTools.Insert(f, st, new Hook { Key = key }, false)) continue;
                bornes[key] = f;
                n++;
            }
            if (n > 0) Log.Info("fils : " + n + " bornes de la SATSUMA suivies (" + bornes.Count + " en tout)");
        }

        public static void Update()
        {
            if (!Game.Msc || nextScan < 0f || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextScan) { nextScan = now + 20f; Scan(); }
            for (int i = relight.Count - 1; i >= 0; i--)
                if (Time.frameCount > relight[i].Value) { if (relight[i].Key != null) relight[i].Key.SetActive(false); relight.RemoveAt(i); }
        }

        // Controle de l'hote (PartsMsc, empreinte 2 s, 27/10) : fil Installed chez l'hote et pas ici -- sa borne (db_ThisPart) est posee en 'Finish assembly'.
        public static bool ApplyByDb(GameObject dbEntry, int who)
        {
            if (bornes.Count == 0) Scan();
            foreach (KeyValuePair<string, PlayMakerFSM> kv in bornes)
            {
                HutongGames.PlayMaker.FsmGameObject tp = kv.Value != null ? kv.Value.FsmVariables.FindFsmGameObject("db_ThisPart") : null;
                if (tp == null || tp.Value != dbEntry) continue;
                Apply(kv.Key, kv.Value, who, "controle de l'hote");
                return true;
            }
            Log.Warn("fils : " + dbEntry.name + " branche chez l'hote, borne introuvable ici");
            return false;
        }

        public static void OnMessage(int who, NetReader r)
        {
            string key = r.Str();
            PlayMakerFSM f;
            if (!bornes.TryGetValue(key, out f) || f == null) { Scan(); bornes.TryGetValue(key, out f); }
            if (f == null) { Log.Warn("fils : " + key + " branche par #" + who + ", borne introuvable ici"); return; }
            Apply(key, f, who, "");
        }
        static void Apply(string key, PlayMakerFSM f, int who, string how)
        {
            if (f.ActiveStateName == "Finish assembly") { Log.Info("fils : " + key + " branche par #" + who + " : deja fait ici"); return; }
            GameObject go = f.gameObject;
            bool wasOff = !go.activeSelf;
            applying = true; Replay.Depth++;
            try
            {
                if (wasOff) go.SetActive(true);
                Game.SetState(f, "Finish assembly");
            }
            catch (System.Exception e) { Log.Warn("fils : " + key + " : " + e.Message); }
            finally { applying = false; Replay.Depth--; }
            if (wasOff) relight.Add(new KeyValuePair<GameObject, int>(go, Time.frameCount + 2));
            Log.Info("fils : " + key + " branche par #" + who + (how.Length > 0 ? " (" + how + ")" : "") + (wasOff ? " (borne allumee le temps du rejeu)" : ""));
        }
    }
}
