using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Plaque VIN de la CORRIS (CARPARTS/VINPlate, automate 'Logic', table PlayMakerHashTableProxy) : a la premiere partie,
    // le jeu tire au hasard la finition (L, LX, SLX, GT), les couleurs, l'interieur, les sieges, la radio, le pare-brise,
    // les roues... et les garde dans cette table (sauvegardee). Les pieces (celles des colis comprises) prennent leur
    // matiere d'apres elle. Tiree chez chacun, la table differait : sieges en cuir marron chez l'un, bleus chez l'autre
    // (retour d'un joueur, 09/10 : « les pieces du colis, la couleur aussi »). Ici l'invite demande la table de l'hote
    // (@vin?), l'hote la lui envoie (@vin), l'invite la recopie et rejoue l'application ("Plant 2" : les etats "... 2"
    // lisent la table et posent les proprietes).
    public static class Vin
    {
        static PlayMakerFSM logic;
        static PlayMakerHashTableProxy table;
        static float askAt = -1, nextFind;
        static bool applied;

        public static void OnLevelLoaded()
        {
            logic = null; table = null; applied = false; nextFind = 0;
            askAt = PlayerSync.InGame ? Time.realtimeSinceStartup + 14f : -1;
        }

        static bool Find()
        {
            if (logic != null && table != null) return true;
            if (Time.realtimeSinceStartup < nextFind) return false;
            nextFind = Time.realtimeSinceStartup + 5f;
            GameObject v = Game.FindAny("CARPARTS/VINPlate");
            if (v == null) return false;
            logic = Game.FsmOn(v, "Logic");
            table = v.GetComponent<PlayMakerHashTableProxy>();
            return logic != null && table != null && table.hashTable != null;
        }

        public static void Update()
        {
            if (askAt < 0 || Time.realtimeSinceStartup < askAt || Session.IsHost || !Session.Active) return;
            if (!Find() || logic.ActiveStateName != "Idle") { askAt = Time.realtimeSinceStartup + 3f; return; }   // (table pas encore faite ici)
            askAt = Time.realtimeSinceStartup + 30f;   // (redemandee si la reponse se perd)
            if (applied) { askAt = -1; return; }
            string fake = Config.Get("Test", "VinFausser", "");   // (essais : table differente ici, "Seats=1")
            if (fake.Contains("=")) { string[] kv = fake.Split('='); table.hashTable[kv[0]] = kv[1]; Log.Info("vin : essai, " + fake + " ici"); }
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@vin?"), true);
            Log.Info("vin : table de l'hote demandee");
        }

        static string Enc(object v)
        {
            if (v == null) return "n:";
            if (v is string) return "s:" + v;
            if (v is int) return "i:" + v;
            if (v is float) return "f:" + ((float)v).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            if (v is bool) return "b:" + v;
            return null;   // (objet : pas transmissible)
        }
        static bool Dec(string s, out object v)
        {
            v = null;
            if (s.Length < 2) return false;
            string body = s.Substring(2);
            switch (s[0])
            {
                case 'n': return true;
                case 's': v = body; return true;
                case 'i': int i; if (int.TryParse(body, out i)) { v = i; return true; } return false;
                case 'f': float f; if (float.TryParse(body, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f)) { v = f; return true; } return false;
                case 'b': v = body == "True"; return true;
            }
            return false;
        }

        // Hote : envoie sa table a celui qui la demande.
        public static void OnAsk(int who)
        {
            if (!Session.IsHost || !Find()) return;
            var keys = new List<string>(); var vals = new List<string>();
            foreach (DictionaryEntry e in table.hashTable)
            {
                string ev = Enc(e.Value);
                if (e.Key == null || ev == null) continue;
                keys.Add(e.Key.ToString()); vals.Add(ev);
            }
            var w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@vin").U16(keys.Count);
            for (int i = 0; i < keys.Count; i++) w.Str(keys[i]).Str(vals[i]);
            Session.SendAll(w, true);
            Log.Info("vin : table envoyee (" + keys.Count + " entrees) a la demande de #" + who);
        }

        // Invite : la table de l'hote, recopiee puis appliquee.
        public static void OnTable(int who, NetReader r)
        {
            int n = r.U16();
            var got = new Dictionary<string, object>();
            for (int i = 0; i < n; i++) { string k = r.Str(), s = r.Str(); object v; if (Dec(s, out v)) got[k] = v; }
            if (Session.IsHost || applied || !Find()) return;
            int diff = 0;
            var diffs = new List<string>();
            foreach (KeyValuePair<string, object> kv in got)
            {
                object mine = table.hashTable.ContainsKey(kv.Key) ? table.hashTable[kv.Key] : null;
                if (Equals(mine, kv.Value)) continue;
                diff++;
                if (diffs.Count < 12) diffs.Add(kv.Key + " " + mine + " -> " + kv.Value);
                table.hashTable[kv.Key] = kv.Value;
            }
            applied = true;
            if (diff == 0) { Log.Info("vin : table identique a celle de l'hote (" + n + " entrees)"); return; }
            Game.SetState(logic, "Plant 2");   // (rejoue la pose : finition, couleurs, interieur, sieges...)
            Log.Info("vin : " + diff + " entrees differaient de l'hote, recopiees et appliquees : " + string.Join(" ; ", diffs.ToArray()));
        }

        // Essais : contenu de la table (journal).
        public static string Describe()
        {
            if (!Find()) return "vin : plaque introuvable";
            var sb = new System.Text.StringBuilder("vin [" + logic.ActiveStateName + "] :");
            foreach (DictionaryEntry e in table.hashTable) sb.Append(' ').Append(e.Key).Append('=').Append(e.Value).Append(e.Value != null ? "(" + e.Value.GetType().Name + ")" : "");
            foreach (string st in new[] { "Seats 2", "Interior", "Colors 2", "Windshield 2" })
            {
                FsmState s = logic.Fsm.GetState(st);
                if (s == null) continue;
                foreach (FsmStateAction a in s.Actions)
                {
                    System.Reflection.FieldInfo pf = a != null ? a.GetType().GetField("targetProperty") : null;
                    FsmProperty fp = pf != null ? pf.GetValue(a) as FsmProperty : null;
                    if (fp != null) sb.Append(" | ").Append(st).Append(" : ").Append(fp.TargetObject != null && fp.TargetObject.Value != null ? fp.TargetObject.Value.GetType().Name + " " + fp.TargetObject.Value.name : "?").Append('.').Append(fp.PropertyName);
                }
            }
            return sb.ToString();
        }
    }
}
